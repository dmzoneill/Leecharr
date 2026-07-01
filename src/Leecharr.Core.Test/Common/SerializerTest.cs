using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Serializer;

namespace Leecharr.Core.Test.Common;

public class SampleModel
{
    public string Name { get; set; } = string.Empty;
    public int Value { get; set; }
    public string NullProperty { get; set; } = null!;
}

[TestFixture]
public class SerializerTest
{
    [Test]
    public void ToJson_SerializesWithCamelCaseAndOmitsNulls()
    {
        var model = new SampleModel { Name = "Test", Value = 42 };
        var json = model.ToJson();

        json.Should().Contain("\"name\":\"Test\"");
        json.Should().Contain("\"value\":42");
        json.Should().NotContain("nullProperty");
    }

    [Test]
    public void FromJson_DeserializesCorrectly()
    {
        var json = "{\"name\":\"Sample\",\"value\":100}";
        var model = json.FromJson<SampleModel>();

        model.Should().NotBeNull();
        model!.Name.Should().Be("Sample");
        model.Value.Should().Be(100);
    }
}
