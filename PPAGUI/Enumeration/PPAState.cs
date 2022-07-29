namespace PPAGUI.Enumeration
{
    public enum PPAState
    {
        PlaceUnit,
        ScanUnitSerialNumber,
        CheckUnitStatus,
        UnitNotFound,
        ScanPcbaOrPumpSerialNumber,
        ScanPcbaSerialNumber,
        ScanPumpSerialNumber,
        UpdateMoveInMove,
        MoveSuccess,
        MoveInOkMoveFail,
        MoveInFail,
        WrongOperation,
        ComponentNotFound,
        Done,
        WrongComponent,
        WrongProductionOrder,
        WaitPreparation,
        ComponentIssueFailed,
        SamePcba,
        SamePump
    }
}
