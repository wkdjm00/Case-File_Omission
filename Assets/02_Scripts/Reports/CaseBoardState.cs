using System;
using System.Collections.Generic;
using System.Linq;

namespace CaseFileOmission.Reports
{
    /// <summary>사건보드 위 항목 하나의 보드 표현. 원본 자료(보고서·단서 소유)와는 별개다.</summary>
    [Serializable]
    public sealed class CaseBoardItem
    {
        public string ItemId;

        /// <summary>장식용 종이·소품. 실제 단서·증거·보고서 답변·회의 카드로 취급하지 않는다.</summary>
        public bool BoardDecorationOnly;

        /// <summary>보드 영역 기준 정규화 좌표(0~1). 왼쪽 아래가 (0, 0).</summary>
        public float X, Y;
        public float RotationDegrees;
        public bool OnBoard = true;
    }

    /// <summary>플레이어가 직접 연결한 자료 한 쌍. 순서 없는 양방향 추론이며 실제 진실·공식 결론에 영향이 없다.</summary>
    [Serializable]
    public sealed class CaseBoardLink
    {
        public string ItemA, ItemB;

        public bool Connects(string a, string b) => (ItemA == a && ItemB == b) || (ItemA == b && ItemB == a);
        public bool Touches(string id) => ItemA == id || ItemB == id;
    }

    /// <summary>
    /// UI-003 사건보드 상태(09 2026-11-13 「사건보드 자료 불러오기·배치·연결선·중앙 메모 구현 계약」).
    /// - 보고서에 보유한 자료마다 보드 항목 하나(같은 자료 중복 금지). 최초 배치는 사건 seed + layout_version으로 결정적.
    /// - 폐기는 보드 배치에서만 숨긴다. 원본 자료·단서 보유·회의 카드 자격은 건드리지 않으며 다시 가져올 수 있다.
    /// - 폐기하면 그 항목의 연결선을 모두 지운다. 연결선은 쌍마다 하나, 플레이어 입력으로만 생긴다.
    /// - 중앙 메모는 연결선이 하나 이상일 때만 보이고, 남은 연결만 정리한다(새 사실을 만들지 않는다).
    /// </summary>
    [Serializable]
    public sealed class CaseBoardState
    {
        public const int LayoutVersion = 1;

        /// <summary>보드 정규화 좌표에서 중앙 메모가 차지하는 사각형(겹치지 않게 배치).</summary>
        public const float MemoMinX = 0.36f, MemoMaxX = 0.64f, MemoMinY = 0.38f, MemoMaxY = 0.62f;

        /// <summary>항목 중심이 놓일 수 있는 범위(보드 가장자리 여백). 아래쪽 폐기 영역은 보드 바깥이다.</summary>
        public const float EdgeMargin = 0.08f;
        public const float MinSpacing = 0.16f;

        public string ContractId;
        public int Seed;
        public int AppliedLayoutVersion = LayoutVersion;
        public readonly List<CaseBoardItem> Items = new List<CaseBoardItem>();
        public readonly List<CaseBoardLink> Links = new List<CaseBoardLink>();

        public CaseBoardState(string contractId, int seed)
        {
            if (string.IsNullOrEmpty(contractId)) throw new ArgumentException("contractId는 비어 있을 수 없습니다.", nameof(contractId));
            ContractId = contractId;
            Seed = seed;
        }

        public CaseBoardItem Get(string itemId) => Items.FirstOrDefault(i => i.ItemId == itemId);

        public IEnumerable<CaseBoardItem> VisibleItems => Items.Where(i => i.OnBoard);

        public bool IsMemoVisible => Links.Count > 0;

        /// <summary>
        /// 보고서 자료를 보드로 불러온다. 이미 있는 자료는 위치를 바꾸지 않는다(재진입 시 새 배치를 만들지 않는다).
        /// 새 자료만 seed 기반 난수로 흩어 놓는다. 장식 항목은 처음 한 번만 decorationCount개 만든다.
        /// </summary>
        public void SyncMaterials(IEnumerable<string> heldMaterialIds, int decorationCount = 3)
        {
            bool first = Items.Count == 0;
            // 같은 사건·같은 보드 상태에서 같은 자료를 불러오면 항상 같은 위치가 나온다.
            var random = new Random(unchecked(Seed * 31 + LayoutVersion * 7919 + Items.Count));

            foreach (var id in heldMaterialIds.Distinct())
            {
                if (Get(id) != null) continue;
                Items.Add(Place(new CaseBoardItem { ItemId = id }, random));
            }

            if (first)
            {
                for (int i = 0; i < decorationCount; i++)
                    Items.Add(Place(new CaseBoardItem { ItemId = $"decoration-{i}", BoardDecorationOnly = true }, random));
            }
        }

        public bool Move(string itemId, float x, float y)
        {
            var item = Get(itemId);
            if (item == null || !item.OnBoard) return false;
            item.X = Clamp(x, EdgeMargin, 1f - EdgeMargin);
            item.Y = Clamp(y, EdgeMargin, 1f - EdgeMargin);
            return true;
        }

        /// <summary>폐기 영역에 놓았을 때. 보드에서만 숨기고 연결선을 지운다.</summary>
        public bool Discard(string itemId)
        {
            var item = Get(itemId);
            if (item == null || !item.OnBoard) return false;
            item.OnBoard = false;
            Links.RemoveAll(l => l.Touches(itemId));
            return true;
        }

        /// <summary>보고서에 남아 있는 자료를 다시 보드에 붙인다(같은 ID, 새 인스턴스 없음).</summary>
        public bool Restore(string itemId)
        {
            var item = Get(itemId);
            if (item == null || item.OnBoard || item.BoardDecorationOnly) return false;
            item.OnBoard = true;
            return true;
        }

        public bool Connect(string a, string b)
        {
            if (a == b) return false;
            var ia = Get(a);
            var ib = Get(b);
            if (ia == null || ib == null || !ia.OnBoard || !ib.OnBoard) return false;
            if (ia.BoardDecorationOnly || ib.BoardDecorationOnly) return false;
            if (Links.Any(l => l.Connects(a, b))) return false;
            Links.Add(new CaseBoardLink { ItemA = a, ItemB = b });
            return true;
        }

        public bool Disconnect(string a, string b) => Links.RemoveAll(l => l.Connects(a, b)) > 0;

        /// <summary>중앙 메모 줄. 연결된 두 자료의 제목만 나란히 적는다(관계를 추측해 보충하지 않는다).</summary>
        public IReadOnlyList<string> BuildMemoLines(Func<string, string> titleOf)
        {
            if (!IsMemoVisible) return Array.Empty<string>();
            return Links.Select(l => $"{titleOf(l.ItemA)} — {titleOf(l.ItemB)}").ToList();
        }

        private CaseBoardItem Place(CaseBoardItem item, Random random)
        {
            const int attempts = 60;
            float bestX = 0.5f, bestY = 0.2f, bestScore = float.MinValue;
            for (int i = 0; i < attempts; i++)
            {
                float x = (float)(EdgeMargin + random.NextDouble() * (1f - 2f * EdgeMargin));
                float y = (float)(EdgeMargin + random.NextDouble() * (1f - 2f * EdgeMargin));
                if (x > MemoMinX && x < MemoMaxX && y > MemoMinY && y < MemoMaxY) continue;

                float nearest = Items.Where(o => o.OnBoard).Select(o => Distance(o.X, o.Y, x, y)).DefaultIfEmpty(1f).Min();
                if (nearest >= MinSpacing) { bestX = x; bestY = y; bestScore = float.MaxValue; break; }
                if (nearest > bestScore) { bestScore = nearest; bestX = x; bestY = y; }
            }
            item.X = bestX;
            item.Y = bestY;
            item.RotationDegrees = (float)(random.NextDouble() * 16.0 - 8.0);
            return item;
        }

        private static float Distance(float ax, float ay, float bx, float by) =>
            (float)Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

        private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
    }
}
