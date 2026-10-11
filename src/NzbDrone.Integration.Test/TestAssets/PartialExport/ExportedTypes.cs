namespace Leecharr.TestAssets;

public class GoodExportedType
{
    public int Value { get; set; } = 1;
}

public class BadExportedType : IMissingContract
{
    public void M()
    {
    }
}
