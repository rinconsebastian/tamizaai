using System.Text.Json;
using Tamiza.Api.Data;
using Tamiza.Api.Kobo;

namespace Tamiza.Api.UnitTests.Kobo;

public sealed class KoboFormTests
{
    public const string CompleteForm = """
        {
          "name": "Household survey 2026",
          "content": {
            "survey": [
              { "type": "start", "name": "start", "$xpath": "start" },
              { "type": "end", "name": "end", "$xpath": "end" },
              { "type": "username", "name": "username", "$xpath": "username" },
              { "type": "begin_group", "name": "household", "$xpath": "household" },
              { "type": "select_one municipalities", "name": "municipality", "$xpath": "household/municipality", "label": ["Municipality", "Municipio"] },
              { "type": "integer", "$autoname": "age", "label": "Age" },
              { "type": "end_group", "$autoname": "household_end" },
              { "type": "begin repeat", "name": "members" },
              { "type": "text", "name": "member_name", "$xpath": "members/member_name" },
              { "type": "end repeat" }
            ]
          }
        }
        """;

    [Fact]
    public void Reads_the_name_and_fields_skipping_groups_and_repeats()
    {
        var asset = KoboForm.Parse(JsonDocument.Parse(CompleteForm).RootElement);

        Assert.Equal("Household survey 2026", asset.Name);
        Assert.Equal(["start", "end", "username", "municipality", "age", "member_name"], asset.Fields.Select(f => f.Name));
        Assert.Equal(new FormField("municipality", "household/municipality", "select_one_municipalities", "Municipality"), asset.Fields[3]);
    }

    [Fact]
    public void Falls_back_to_autoname_and_name_for_missing_xpath()
    {
        var age = KoboForm.Parse(JsonDocument.Parse(CompleteForm).RootElement).Fields.Single(f => f.Name == "age");
        Assert.Equal(("age", "Age"), (age.Xpath, age.Label));
    }

    [Fact]
    public void A_document_without_content_has_no_fields() =>
        Assert.Empty(KoboForm.Parse(JsonDocument.Parse("""{ "name": "Empty" }""").RootElement).Fields);
}
