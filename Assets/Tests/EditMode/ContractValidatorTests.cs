using System.Collections.Generic;
using CaseFileOmission.Contracts;
using CaseFileOmission.Rooms;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class ContractValidatorTests
    {
        [Test]
        public void Validate_ConsistentIndoorContract_PassesLocationCheck()
        {
            var contract = new ContractInstance("c1")
            {
                PrimaryCrimeType = CrimeType.Murder,
                LocationCategory = LocationCategory.Indoor,
                Location = StubLocation.House
            };
            contract.Truth.TimelineEvents.Add("사건 발생");

            var result = new ContractValidator().Validate(contract);

            Assert.IsFalse(result.Errors.Contains(ContractValidationErrorCode.LOCATION_INVALID));
        }

        [Test]
        public void Validate_MismatchedLocation_FailsWithLocationInvalid()
        {
            var contract = new ContractInstance("c1")
            {
                PrimaryCrimeType = CrimeType.Murder,
                LocationCategory = LocationCategory.Indoor,
                Location = StubLocation.StubOutdoor
            };
            contract.Truth.TimelineEvents.Add("사건 발생");

            var result = new ContractValidator().Validate(contract);

            Assert.Contains(ContractValidationErrorCode.LOCATION_INVALID, result.Errors);
        }

        [Test]
        public void Validate_SubCrimeDuplicatesPrimary_FailsWithContradiction()
        {
            var contract = new ContractInstance("c1")
            {
                PrimaryCrimeType = CrimeType.Murder,
                LocationCategory = LocationCategory.Indoor,
                Location = StubLocation.House
            };
            contract.SubCrimes.Add(CrimeType.Murder);
            contract.Truth.TimelineEvents.Add("사건 발생");

            var result = new ContractValidator().Validate(contract);

            Assert.Contains(ContractValidationErrorCode.CONTRADICTION, result.Errors);
        }

        [Test]
        public void Validate_RecoverBodyConditionOnMissingCase_FailsWithContractInvalid()
        {
            var contract = new ContractInstance("c1")
            {
                PrimaryCrimeType = CrimeType.Missing,
                LocationCategory = LocationCategory.Indoor,
                Location = StubLocation.House
            };
            contract.OrgCondition.Type = OrganizationConditionType.RecoverBody;
            contract.Truth.TimelineEvents.Add("실종 발생");

            var result = new ContractValidator().Validate(contract);

            Assert.Contains(ContractValidationErrorCode.CONTRACT_INVALID, result.Errors);
        }

        [Test]
        public void Validate_EmptyTimeline_FailsWithTimelineConflict()
        {
            var contract = new ContractInstance("c1")
            {
                PrimaryCrimeType = CrimeType.Murder,
                LocationCategory = LocationCategory.Indoor,
                Location = StubLocation.House
            };

            var result = new ContractValidator().Validate(contract);

            Assert.Contains(ContractValidationErrorCode.TIMELINE_CONFLICT, result.Errors);
        }

        [Test]
        public void Validate_FullyConsistentContract_IsValid()
        {
            var contract = new ContractInstance("c1")
            {
                PrimaryCrimeType = CrimeType.Blackmail,
                LocationCategory = LocationCategory.Indoor,
                Location = StubLocation.House
            };
            contract.Truth.TimelineEvents.Add("협박 발생");

            var result = new ContractValidator().Validate(contract);

            Assert.IsTrue(result.IsValid);
        }

        [Test]
        public void Validate_OutdoorCategory_AlwaysInvalid()
        {
            // 사용자 지시(2026-11-09): 의뢰 사건 공간에서 실외를 제외한다. Location이 내부적으로
            // 일치해 보여도(Outdoor+StubOutdoor) 실외 자체가 더 이상 유효한 공간이 아니다.
            var contract = new ContractInstance("c1")
            {
                PrimaryCrimeType = CrimeType.Blackmail,
                LocationCategory = LocationCategory.Outdoor,
                Location = StubLocation.StubOutdoor
            };
            contract.Truth.TimelineEvents.Add("협박 발생");

            var result = new ContractValidator().Validate(contract);

            Assert.Contains(ContractValidationErrorCode.LOCATION_INVALID, result.Errors);
        }

        [Test]
        public void Validate_ToolWithoutMethod_FailsWithContradiction()
        {
            var contract = new ContractInstance("c1")
            {
                PrimaryCrimeType = CrimeType.Murder,
                LocationCategory = LocationCategory.Indoor,
                Location = StubLocation.House
            };
            contract.Truth.TimelineEvents.Add("사건 발생");
            contract.Truth.Tool = "둔기";
            // Method를 설정하지 않음

            var result = new ContractValidator().Validate(contract);

            Assert.Contains(ContractValidationErrorCode.CONTRADICTION, result.Errors);
        }

        [Test]
        public void Validate_ToolWithMethod_Passes()
        {
            var contract = new ContractInstance("c1")
            {
                PrimaryCrimeType = CrimeType.Murder,
                LocationCategory = LocationCategory.Indoor,
                Location = StubLocation.House
            };
            contract.Truth.TimelineEvents.Add("사건 발생");
            contract.Truth.Tool = "둔기";
            contract.Truth.Method = "둔기로 가격";

            var result = new ContractValidator().Validate(contract);

            Assert.IsTrue(result.IsValid);
        }

        [Test]
        public void ValidateSpatial_PerpetratorAndVictimSameRoom_Passes()
        {
            var contract = new ContractInstance("c1");
            contract.Truth.PerpetratorId = "perp";
            contract.Truth.VictimId = "victim";

            var rooms = HouseLayoutFactory.CreateStandardHouse();
            var assignments = new Dictionary<string, string>
            {
                ["perp"] = HouseLayoutFactory.LivingRoom,
                ["victim"] = HouseLayoutFactory.LivingRoom
            };

            var result = new ContractValidator().ValidateSpatial(contract, rooms, assignments);

            Assert.IsTrue(result.IsValid);
        }

        [Test]
        public void ValidateSpatial_PerpetratorAndVictimUnreachable_FailsWithContradiction()
        {
            var contract = new ContractInstance("c1");
            contract.Truth.PerpetratorId = "perp";
            contract.Truth.VictimId = "victim";

            // 연결되지 않은 방 2개만 있는 그래프를 직접 구성한다.
            var rooms = new RoomGraph();
            rooms.AddRoom(new RoomDefinition("room-a"));
            rooms.AddRoom(new RoomDefinition("room-b"));

            var assignments = new Dictionary<string, string>
            {
                ["perp"] = "room-a",
                ["victim"] = "room-b"
            };

            var result = new ContractValidator().ValidateSpatial(contract, rooms, assignments);

            Assert.Contains(ContractValidationErrorCode.CONTRADICTION, result.Errors);
        }

        [Test]
        public void ValidateSpatial_MissingAssignments_SkipsCheck()
        {
            var contract = new ContractInstance("c1");
            contract.Truth.PerpetratorId = "perp";
            contract.Truth.VictimId = "victim";

            var rooms = HouseLayoutFactory.CreateStandardHouse();
            var assignments = new Dictionary<string, string>(); // 아직 배치되지 않음

            var result = new ContractValidator().ValidateSpatial(contract, rooms, assignments);

            Assert.IsTrue(result.IsValid, "위치가 아직 배정되지 않았다면 이 검사는 보류해야 한다.");
        }
    }
}
