using System;
using System.Linq;
using CaseFileOmission.Contracts;
using CaseFileOmission.ContractContent;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    /// <summary>04 2026-11-12 대표 세부 패턴(REP-SUB)·실제 세부 종류(ACT-SUB) 최종 확정 — 생성과 검증 연결.</summary>
    public class SubtypeCompatibilityTests
    {
        [Test]
        public void Catalog_Has20RepresentativeAnd20ActualSubtypes()
        {
            Assert.AreEqual(20, CaseCatalog.RepresentativeSubtypes.Values.Sum(v => v.Length));
            Assert.AreEqual(20, CaseCatalog.ActualSubtypes.Values.Sum(v => v.Length));
            foreach (CrimeType type in Enum.GetValues(typeof(CrimeType)))
            {
                Assert.AreEqual(4, CaseCatalog.RepresentativeSubtypes[type].Length, type.ToString());
                Assert.AreEqual(4, CaseCatalog.ActualSubtypes[type].Length, type.ToString());
            }
        }

        [Test]
        public void GeneratedContracts_PassSubtypeValidation_AndFollowPriorityRules()
        {
            for (int seed = 0; seed < 80; seed++)
            {
                var built = new ContractPipeline(ContractGenerationConfig.CreateDefault(), new Random(seed)).Generate(() => $"c{seed}");
                var c = built.Contract;
                string where = $"seed {seed}: {c.PoliceCrimeType}→{c.PrimaryCrimeType} {c.Truth.RepresentativeSubtypeId}/{c.Truth.ActualSubtypeId}";

                Assert.IsTrue(CaseDetailGenerator.ValidateSubtypes(built), where);
                Assert.IsTrue(CaseCatalog.RepresentativeSubtypes[c.PoliceCrimeType].Any(e => e.Id == c.Truth.RepresentativeSubtypeId), where);
                Assert.IsTrue(CaseCatalog.ActualSubtypes[c.PrimaryCrimeType].Any(e => e.Id == c.Truth.ActualSubtypeId), where);

                // 중복 판정 우선순위: 생존 후 사망 → MUR-02, 인계 시점 시신 미발견 → MUR-04.
                if (c.Truth.DelayedDeath) Assert.AreEqual("ACT-SUB-MUR-02", c.Truth.ActualSubtypeId, where);
                else if (c.Truth.BodyHidden) Assert.AreEqual("ACT-SUB-MUR-04", c.Truth.ActualSubtypeId, where);
                Assert.AreNotEqual("ACT-SUB-ASS-02", c.Truth.ActualSubtypeId, "반복 공격 경위는 만들지 않는다");

                // 경찰이 사망을 확인하지 못한 살인 분류는 사망을 확정적으로 말하는 패턴(MUR-01·02·03)을 쓰지 않는다.
                if (c.PoliceCrimeType == CrimeType.Murder && c.PrimaryCrimeType != CrimeType.Murder)
                    Assert.AreEqual("REP-SUB-MUR-04", c.Truth.RepresentativeSubtypeId, where);
                if (c.Truth.RepresentativeSubtypeId == "REP-SUB-MUR-03")
                    Assert.That(c.Truth.CoverUp, Is.EqualTo(CoverUpMethod.AccidentalDeathDisguise).Or.EqualTo(CoverUpMethod.SuicideDisguise), where);
            }
        }
    }
}
