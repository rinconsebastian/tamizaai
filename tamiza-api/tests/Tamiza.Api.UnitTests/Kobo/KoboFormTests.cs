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
    public void Builds_the_path_from_the_group_nesting_when_the_row_has_no_xpath()
    {
        // `age` has no $xpath and sits inside the group `household`, so its answer is keyed `household/age`.
        var age = KoboForm.Parse(JsonDocument.Parse(CompleteForm).RootElement).Fields.Single(f => f.Name == "age");
        Assert.Equal(("household/age", "Age"), (age.Xpath, age.Label));
    }

    [Fact]
    public void A_question_in_a_group_has_its_default_language_label_and_the_nested_path()
    {
        // English is the default language (first); the group is not a field, and the path comes from the nesting.
        const string form = """
            {
              "name": "Groups",
              "content": {
                "translations": ["English (en)", "Spanish (es)"],
                "survey": [
                  { "type": "begin_group", "name": "respondent", "label": ["Respondent", "Encuestado"] },
                  { "type": "integer", "name": "age", "label": ["Age of the respondent", "Edad del encuestado"] },
                  { "type": "end_group" }
                ]
              }
            }
            """;

        var asset = KoboForm.Parse(JsonDocument.Parse(form).RootElement);

        Assert.Equal([new FormField("age", "respondent/age", "integer", "Age of the respondent")], asset.Fields);
    }

    [Fact]
    public void Questions_inside_a_repeat_group_are_marked_also_through_a_plain_group_but_a_plain_group_alone_is_not()
    {
        const string form = """
            {
              "name": "Repeats",
              "content": {
                "survey": [
                  { "type": "begin_group", "name": "respondent" },
                  { "type": "text", "name": "municipality" },
                  { "type": "end_group" },
                  { "type": "begin_repeat", "name": "members" },
                  { "type": "integer", "name": "member_age" },
                  { "type": "begin_group", "name": "health" },
                  { "type": "text", "name": "condition" },
                  { "type": "end_group" },
                  { "type": "end_repeat" },
                  { "type": "integer", "name": "household_size" }
                ]
              }
            }
            """;

        var fields = KoboForm.Parse(JsonDocument.Parse(form).RootElement).Fields;

        Assert.Equal(
            ["respondent/municipality=False", "members/member_age=True", "members/health/condition=True", "household_size=False"],
            fields.Select(f => $"{f.Xpath}={f.InRepeat}"));
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

    [Fact]
    public void Labels_come_only_from_the_default_language()
    {
        // Spanish is the default language (first); the choice `3` has an English label but no Spanish one.
        const string form = """
            {
              "name": "Languages",
              "content": {
                "translations": ["Spanish (es)", "English (en)"],
                "survey": [
                  { "type": "select_one", "select_from_list_name": "wells", "name": "well", "label": [null, "Well"] },
                  { "type": "select_one", "select_from_list_name": "wells", "name": "other_well", "label": ["", "Other well"] }
                ],
                "choices": [
                  { "list_name": "wells", "name": "1", "label": ["Pozo", "Well"] },
                  { "list_name": "wells", "name": "3", "label": [null, "Tank"] },
                  { "list_name": "wells", "name": "4", "label": [] },
                  { "list_name": "wells", "name": "5" }
                ]
              }
            }
            """;

        var asset = KoboForm.Parse(JsonDocument.Parse(form).RootElement);

        Assert.Equal([null, null], asset.Fields.Select(f => f.Label));
        Assert.Equal(
            [new FormChoice("wells", "1", "Pozo"), new FormChoice("wells", "3", null), new FormChoice("wells", "4", null), new FormChoice("wells", "5", null)],
            asset.Choices);
    }

    [Fact]
    public void Reads_the_shape_of_old_documents()
    {
        // Plain string labels, types given as objects, no $xpath anywhere.
        const string form = """
            {
              "name": "Old form",
              "content": {
                "survey": [
                  { "type": "begin_group", "name": "respondent", "label": "Respondent" },
                  { "type": { "select_one": "municipalities" }, "name": "municipality", "label": "Municipality" },
                  { "type": { "select_multiple": "services" }, "name": "services", "label": "Services" },
                  { "type": { "text": true }, "name": "notes" },
                  { "type": "end_group" },
                  { "type": "text", "name": "comment" }
                ],
                "choices": [
                  { "list_name": "municipalities", "name": "cali", "label": "Cali" }
                ]
              }
            }
            """;

        var asset = KoboForm.Parse(JsonDocument.Parse(form).RootElement);

        Assert.Equal(["respondent/municipality", "respondent/services", "respondent/notes", "comment"], asset.Fields.Select(f => f.Xpath));
        Assert.Equal(
            [new FormField("municipality", "respondent/municipality", "select_one_municipalities", "Municipality", "municipalities"),
             new FormField("services", "respondent/services", "select_multiple_services", "Services", "services")],
            asset.Fields.Take(2));
        Assert.Equal("text", asset.Fields[2].Type);
        Assert.Equal([new FormChoice("municipalities", "cali", "Cali")], asset.Choices);
    }

    [Fact]
    public void Builds_paths_through_every_kind_of_nesting_and_leaves_them_at_the_end_rows()
    {
        const string form = """
            {
              "content": {
                "survey": [
                  { "type": "begin_group", "name": "a" },
                  { "type": "begin_repeat", "name": "members" },
                  { "type": "text", "name": "member_name" },
                  { "type": "end_repeat" },
                  { "type": "begin_score", "name": "scores" },
                  { "type": "score__row", "name": "water" },
                  { "type": "end_score" },
                  { "type": "begin_rank", "name": "ranking" },
                  { "type": "rank__level", "name": "first" },
                  { "type": "end_rank" },
                  { "type": "begin_kobomatrix", "name": "matrix" },
                  { "type": "integer", "name": "cell" },
                  { "type": "end_kobomatrix" },
                  { "type": "text", "name": "after_nesting" },
                  { "type": "end_group" },
                  { "type": "text", "name": "top" },
                  { "type": "end_group" },
                  { "type": "text", "name": "still_top" }
                ]
              }
            }
            """;

        var fields = KoboForm.Parse(JsonDocument.Parse(form).RootElement).Fields;

        Assert.Equal(
            ["a/members/member_name", "a/scores/water", "a/ranking/first", "a/matrix/cell", "a/after_nesting", "top", "still_top"],
            fields.Select(f => f.Xpath));
    }

    [Fact]
    public void Uses_the_names_Kobo_deploys_not_the_stored_xpath()
    {
        // The deployed name of the question is $autoname, and the stored $xpath disagrees with it.
        const string form = """
            {
              "content": {
                "survey": [
                  { "type": "begin_group", "name": "old_group", "$autoname": "household" },
                  { "type": "select_one sex", "name": "gender", "$autoname": "sex", "$xpath": "old_group/gender" }
                ],
                "choices": [
                  { "list_name": "sex", "name": "f_old", "$autovalue": "f", "label": ["Female"] },
                  { "list_name": "sex", "$autovalue": "m", "label": ["Male"] },
                  { "list_name": "sex", "label": ["No name or value"] }
                ]
              }
            }
            """;

        var asset = KoboForm.Parse(JsonDocument.Parse(form).RootElement);

        var field = Assert.Single(asset.Fields);
        Assert.Equal(("sex", "household/sex"), (field.Name, field.Xpath));
        Assert.Equal([new FormChoice("sex", "f", "Female"), new FormChoice("sex", "m", "Male")], asset.Choices);
    }

    [Fact]
    public void Marks_the_questions_with_the_or_other_option()
    {
        const string form = """
            {
              "content": {
                "survey": [
                  { "type": "select_one sources", "name": "water_source", "_or_other": true },
                  { "type": "select_one sources", "name": "sewage", "_or_other": false },
                  { "type": "select_one sources", "name": "waste" }
                ]
              }
            }
            """;

        var fields = KoboForm.Parse(JsonDocument.Parse(form).RootElement).Fields;

        Assert.Equal([true, false, false], fields.Select(f => f.OrOther));
    }

    [Fact]
    public void The_last_of_a_duplicated_choice_wins()
    {
        const string form = """
            {
              "content": {
                "survey": [],
                "choices": [
                  { "list_name": "yes_no", "name": "1", "label": ["Yes"] },
                  { "list_name": "yes_no", "name": "2", "label": ["No"] },
                  { "list_name": "yes_no", "name": "1", "label": ["Sure"] },
                  { "list_name": "other_list", "name": "1", "label": ["Kept"] }
                ]
              }
            }
            """;

        var choices = KoboForm.Parse(JsonDocument.Parse(form).RootElement).Choices;

        Assert.Equal(
            [new FormChoice("yes_no", "1", "Sure"), new FormChoice("yes_no", "2", "No"), new FormChoice("other_list", "1", "Kept")],
            choices);
    }

    [Fact]
    public void Reads_the_latest_and_the_deployed_version_of_the_asset()
    {
        var deployed = KoboForm.Parse(JsonDocument.Parse("""{ "name": "F", "version_id": "vDraft", "deployed_version_id": "vLive" }""").RootElement);
        var undeployed = KoboForm.Parse(JsonDocument.Parse("""{ "name": "F", "version_id": "vDraft", "deployed_version_id": null }""").RootElement);
        var bare = KoboForm.Parse(JsonDocument.Parse("""{ "name": "F" }""").RootElement);

        Assert.Equal(("vDraft", "vLive"), (deployed.VersionId, deployed.DeployedVersionId));
        Assert.Equal(("vDraft", (string?)null), (undeployed.VersionId, undeployed.DeployedVersionId));
        Assert.Equal(((string?)null, (string?)null), (bare.VersionId, bare.DeployedVersionId));
    }

    [Fact]
    public void Reads_a_version_document_with_its_deployment_date()
    {
        const string version = """
            {
              "uid": "vLive",
              "content_hash": "abc",
              "date_modified": "2026-09-01T10:00:00Z",
              "date_deployed": "2026-09-02T08:30:00.123456Z",
              "content": {
                "survey": [{ "type": "select_one yes_no", "name": "has_water", "label": ["Has water"] }],
                "choices": [{ "list_name": "yes_no", "name": "1", "label": ["Yes"] }]
              }
            }
            """;

        var parsed = KoboForm.ParseVersion(JsonDocument.Parse(version).RootElement);

        Assert.Equal("vLive", parsed.Uid);
        Assert.Equal(new DateTimeOffset(2026, 9, 2, 8, 30, 0, TimeSpan.Zero).AddTicks(1234560), parsed.DeployedAt);
        Assert.Equal("has_water", Assert.Single(parsed.Fields).Name);
        Assert.Equal(new FormChoice("yes_no", "1", "Yes"), Assert.Single(parsed.Choices));
    }

    [Fact]
    public void A_draft_version_has_no_deployment_date()
    {
        var parsed = KoboForm.ParseVersion(JsonDocument.Parse("""{ "uid": "vDraft", "date_deployed": false, "content": { "survey": [] } }""").RootElement);

        Assert.Null(parsed.DeployedAt);
    }
}
