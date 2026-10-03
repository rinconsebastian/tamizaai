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
        Assert.Equal(new FormField("municipality", "household/municipality", "select_one_municipalities", "Municipality", "municipalities"), asset.Fields[3]);
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

    [Fact]
    public void Reads_choice_lists_with_their_default_language_label()
    {
        const string form = """
            {
              "name": "Choices",
              "content": {
                "survey": [
                  { "type": "select_one", "select_from_list_name": "municipalities", "name": "municipality", "$xpath": "municipality" },
                  { "type": "select_multiple services", "name": "services", "$xpath": "services" },
                  { "type": "text", "name": "notes", "$xpath": "notes" }
                ],
                "choices": [
                  { "list_name": "municipalities", "name": "cali", "label": ["Cali", "Santiago de Cali"] },
                  { "list_name": "municipalities", "$autovalue": "palmira", "label": ["Palmira"] },
                  { "list_name": "services", "name": "water", "label": "Water" },
                  { "list_name": "services", "label": ["No name"] }
                ]
              }
            }
            """;

        var asset = KoboForm.Parse(JsonDocument.Parse(form).RootElement);

        Assert.Equal(["municipalities", "services", null], asset.Fields.Select(f => f.ListName));
        Assert.Equal("select_one", asset.Fields[0].Type);
        Assert.Equal("select_multiple_services", asset.Fields[1].Type);
        Assert.Equal(
            [new FormChoice("municipalities", "cali", "Cali"), new FormChoice("municipalities", "palmira", "Palmira"), new FormChoice("services", "water", "Water")],
            asset.Choices);
    }

    [Fact]
    public void A_form_without_choices_has_none() =>
        Assert.Empty(KoboForm.Parse(JsonDocument.Parse(CompleteForm).RootElement).Choices);
}
