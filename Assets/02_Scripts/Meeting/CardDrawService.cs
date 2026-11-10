using System;
using System.Collections.Generic;
using System.Linq;

namespace CaseFileOmission.Meeting
{
    /// <summary>
    /// §7-2 "카드 5장 추첨"의 실제 추첨 로직. CardPresentationLimits의 상수(한도)만으로는
    /// 추첨이 일어나지 않으므로, 보유 단서 중에서 무작위로 뽑는 처리를 담당한다.
    /// </summary>
    public static class CardDrawService
    {
        public static IReadOnlyList<string> DrawCards(IReadOnlyList<string> heldClueIds, int limit, Random random)
        {
            if (heldClueIds == null) throw new ArgumentNullException(nameof(heldClueIds));
            if (random == null) throw new ArgumentNullException(nameof(random));

            var pool = heldClueIds.ToList();
            var drawn = new List<string>();
            int drawCount = Math.Min(limit, pool.Count);

            for (int i = 0; i < drawCount; i++)
            {
                int index = random.Next(pool.Count);
                drawn.Add(pool[index]);
                pool.RemoveAt(index);
            }

            return drawn;
        }
    }
}
