using Tamiza.Api.Data;
using Tamiza.Api.Http;
using Tamiza.Api.Projects;

namespace Tamiza.Api.UnitTests.Projects;

public sealed class SamplingFrameTemplateTests
{
    private static FormField SelectOne(string xpath, string list, bool orOther = false, bool inRepeat = false) =>
        new(xpath.Split('/')[^1], xpath, "select_one", xpath, list, orOther, inRepeat);

    private static FormField Typed(string xpath, string type = "integer") => new(xpath.Split('/')[^1], xpath, type, xpath);

    private static List<FormChoice> Choices(string list, params string[] codes) => codes.Select(c => new FormChoice(list, c, $"Label {c}")).ToList();

    private static TemplateDimensionInput Dim(string field, params string?[]? values) => new(field, values?.ToList());

    private static TemplateDimensionInput All(string field) => new(field, null);

    private static (TemplatePlan? Plan, IDictionary<string, string[]> Errors) Plan(
        IReadOnlyList<FormField> fields, IReadOnlyList<FormChoice> choices, params TemplateDimensionInput[] dimensions)
    {
        var errors = new FieldErrors();
        var plan = SamplingFrameTemplate.Plan(new SamplingFrameTemplateRequest(dimensions.ToList()), fields, choices, errors);
        return (plan, errors.ToDictionary());
    }

    private static TemplatePlan Valid(IReadOnlyList<FormField> fields, IReadOnlyList<FormChoice> choices, params TemplateDimensionInput[] dimensions)
    {
        var (plan, errors) = Plan(fields, choices, dimensions);
        Assert.Empty(errors);
        return plan!;
    }

    private static string Refusal(IReadOnlyList<FormField> fields, IReadOnlyList<FormChoice> choices, params TemplateDimensionInput[] dimensions)
    {
        var (plan, errors) = Plan(fields, choices, dimensions);
        Assert.Null(plan);
        return Assert.Single(Assert.Single(errors).Value);
    }

    // Refusals

    [Fact]
    public void Too_many_combinations()
    {
        var fields = new[] { SelectOne("municipio", "m"), SelectOne("sexo", "s"), SelectOne("rango_edad", "e") };
        var choices = Choices("m", Enumerable.Range(1, 1_100).Select(i => $"{i:00000}").ToArray())
            .Concat(Choices("s", "f", "m")).Concat(Choices("e", "1", "2", "3", "4", "5")).ToList();

        var message = Refusal(fields, choices, All("municipio"), All("sexo"), All("rango_edad"));

        Assert.Equal("These choices make 11,000 rows; a sampling frame holds at most 10,000. Choose fewer choices or questions.", message);
    }

    [Fact]
    public void Past_a_million_rows_the_count_is_not_printed_exactly()
    {
        var fields = Enumerable.Range(1, 10).Select(i => Typed($"q{i}")).ToArray();
        var values = Enumerable.Range(1, 400).Select(i => i.ToString()).ToArray();

        var message = Refusal(fields, [], fields.Select(f => Dim(f.Xpath, values)).ToArray());

        Assert.StartsWith("These choices make more than 1,000,000 rows;", message);
    }

    [Fact]
    public void Multiple_choice_question()
    {
        var fields = new[] { new FormField("idiomas", "idiomas", "select_multiple", "Idiomas", "i") };

        var message = Refusal(fields, Choices("i", "es"), All("idiomas"));

        Assert.Equal("Coverage cannot count 'idiomas': an answer can hold several choices.", message);
    }

    [Fact]
    public void Question_inside_a_repeat_group()
    {
        var fields = new[] { SelectOne("miembros/edad", "e", inRepeat: true) };

        Assert.Equal("Coverage cannot count 'edad': it is answered once per repeat.", Refusal(fields, Choices("e", "1"), All("miembros/edad")));
    }

    [Fact]
    public void Question_named_target()
    {
        var fields = new[] { SelectOne("Target", "s") };

        Assert.Equal("'Target' cannot be used: its name is reserved for the target column.", Refusal(fields, Choices("s", "f"), All("Target")));
    }

    [Fact]
    public void Unknown_choice()
    {
        var fields = new[] { SelectOne("municipio", "m") };

        Assert.Equal("'zz' is not a choice of 'municipio'.", Refusal(fields, Choices("m", "05001"), Dim("municipio", "05001", "zz")));
    }

    [Fact]
    public void An_empty_selection_of_choices()
    {
        var fields = new[] { SelectOne("municipio", "m") };

        var (_, errors) = Plan(fields, Choices("m", "05001"), Dim("municipio"));

        Assert.Equal(["Choose at least one choice."], errors["dimensions[0].values"]);
    }

    [Fact]
    public void The_same_question_twice()
    {
        var fields = new[] { SelectOne("sexo", "s") };

        var (_, errors) = Plan(fields, Choices("s", "f"), All("sexo"), All("sexo"));

        Assert.Equal(["'sexo' is chosen more than once."], errors["dimensions[1].field"]);
    }

    [Fact]
    public void A_question_the_form_does_not_have()
    {
        var (_, errors) = Plan([Typed("estrato")], [], Dim("barrio", "x"));

        Assert.Equal(["The form has no question 'barrio'."], errors["dimensions[0].field"]);
    }

    [Fact]
    public void Columns_named_alike_ignoring_case()
    {
        var fields = new[] { Typed("G/edad"), Typed("g/edad") };

        var (_, errors) = Plan(fields, [], Dim("G/edad", "1"), Dim("g/edad", "2"));

        Assert.Equal(["'g/edad' and 'G/edad' would both be named 'g/edad' in the file. Choose one of them."], errors["dimensions[1].field"]);
    }

    [Fact]
    public void A_typed_value_over_500_characters()
    {
        var (_, errors) = Plan([Typed("estrato")], [], Dim("estrato", "1", new string('x', 501)));

        Assert.Equal(["A value of 'estrato' is longer than 500 characters."], errors["dimensions[0].values"]);
    }

    [Fact]
    public void A_typed_value_of_500_characters_is_accepted() =>
        Assert.Equal(1, Valid([Typed("estrato")], [], Dim("estrato", new string('x', 500))).RowCount);

    [Fact]
    public void More_than_ten_questions()
    {
        var fields = Enumerable.Range(1, 11).Select(i => Typed($"q{i}")).ToArray();

        Assert.Equal("Choose between 1 and 10 questions.", Refusal(fields, [], fields.Select(f => Dim(f.Xpath, "1")).ToArray()));
    }

    [Fact]
    public void No_question()
    {
        Assert.Equal("Choose between 1 and 10 questions.", Refusal([Typed("estrato")], []));
    }

    // Cases shared with sampling-frame-fields.spec.ts (headers, counts and first rows).

    [Fact]
    public void A_grouped_target_question_is_named_after_its_path()
    {
        var plan = Valid([SelectOne("G/Target", "s")], Choices("s", "f", "m"), All("G/Target"));

        Assert.Equal("G/Target", plan.Columns[0].Header);
        Assert.Equal(2, plan.RowCount);
        Assert.Equal(["f"], plan.Rows().First());
    }

    [Fact]
    public void A_note_sharing_the_name_gives_the_path()
    {
        var fields = new[] { SelectOne("hogar/edad", "e"), new FormField("edad", "info/edad", "note", "Sobre la edad"), Typed("sexo") };

        var plan = Valid(fields, Choices("e", "1", "2"), All("hogar/edad"), Dim("sexo", "f"));

        Assert.Equal(["hogar/edad", "sexo"], plan.Columns.Select(c => c.Header));
        Assert.Equal(2, plan.RowCount);
        Assert.Equal(["1", "f"], plan.Rows().First());
    }

    [Fact]
    public void Codes_that_differ_only_in_case_are_kept_once()
    {
        var fields = new[] { SelectOne("letra", "l") };
        var choices = Choices("l", "A", "a");

        var all = Valid(fields, choices, All("letra"));
        var chosen = Valid(fields, choices, Dim("letra", "a"));

        Assert.Equal(1, all.RowCount);
        Assert.Equal(["A"], all.Rows().Single());
        Assert.Equal(["A"], chosen.Rows().Single());
    }

    [Fact]
    public void An_or_other_question_offers_other_but_does_not_choose_it_by_default()
    {
        var fields = new[] { SelectOne("sexo", "s", orOther: true) };
        var choices = Choices("s", "f", "m");

        var all = Valid(fields, choices, All("sexo"));
        var explicitly = Valid(fields, choices, Dim("sexo", "f", "m", "other"));

        Assert.Equal(["f", "m", "other"], all.Columns[0].Choices!.Select(c => c.Code));
        Assert.Equal("Other", all.Columns[0].Choices![2].Label);
        Assert.Equal(["f", "m"], all.Columns[0].Values);
        Assert.Equal(2, all.RowCount);
        Assert.Equal(["f", "m", "other"], explicitly.Columns[0].Values);
        Assert.Equal(3, explicitly.RowCount);
    }

    [Fact]
    public void An_or_other_question_whose_list_has_other_adds_nothing()
    {
        var fields = new[] { SelectOne("sexo", "s", orOther: true) };

        var all = Valid(fields, Choices("s", "f", "Other"), All("sexo"));

        Assert.Equal(["f", "Other"], all.Columns[0].Choices!.Select(c => c.Code));
        Assert.Equal(["f", "Other"], all.Columns[0].Values);
        Assert.Equal(2, all.RowCount);
    }

    [Fact]
    public void Typed_values_are_trimmed_and_kept_once()
    {
        var plan = Valid([Typed("estrato")], [], Dim("estrato", "1\n 1\n\n2\n".Split('\n')));

        Assert.Equal(["1", "2"], plan.Columns[0].Values);
        Assert.Equal(2, plan.RowCount);
        Assert.Equal(["1"], plan.Rows().First());
    }

    [Fact]
    public void Every_chosen_list_empty_gives_no_rows()
    {
        var plan = Valid([Typed("barrio", "text"), Typed("estrato")], [], Dim("barrio"), All("estrato"));

        Assert.Equal(0, plan.RowCount);
        Assert.Empty(plan.Rows());
    }

    // Choice lists and rows

    [Fact]
    public void Chosen_codes_follow_form_order_and_are_matched_ignoring_case()
    {
        var plan = Valid([SelectOne("municipio", "m")], Choices("m", "05001", "05088", "Abc"), Dim("municipio", "abc", "05001", "05001"));

        Assert.Equal(["05001", "Abc"], plan.Columns[0].Values);
    }

    [Fact]
    public void Questions_without_a_usable_list_take_typed_values()
    {
        var fields = new[]
        {
            new FormField("finca", "finca", "select_one_from_file fincas.csv", "Finca"),
            new FormField("vacia", "vacia", "select_one", "Vacía", "empty"),
        };

        var plan = Valid(fields, [], Dim("finca", "a"), Dim("vacia", "x"));

        Assert.All(plan.Columns, c => Assert.Null(c.Choices));
        Assert.Equal(1, plan.RowCount);
    }

    [Fact]
    public void Rows_combine_in_request_order_with_the_first_column_slowest_and_empty_columns_blank()
    {
        var fields = new[] { Typed("a", "text"), Typed("b", "text"), Typed("c", "text") };

        var plan = Valid(fields, [], Dim("a", "1", "2"), Dim("b"), Dim("c", "x", "y", "z"));

        Assert.Equal(6, plan.RowCount);
        Assert.Equal(
            [["1", "", "x"], ["1", "", "y"], ["1", "", "z"], ["2", "", "x"], ["2", "", "y"], ["2", "", "z"]],
            plan.Rows().ToArray());
    }

    [Theory]
    [InlineData(new int[0], 0)]
    [InlineData(new[] { 0, 0 }, 0)]
    [InlineData(new[] { 5, 0, 3 }, 15)]
    [InlineData(new[] { int.MaxValue, int.MaxValue, int.MaxValue }, long.MaxValue)]
    public void Row_counts_saturate(int[] lengths, long expected) => Assert.Equal(expected, SamplingFrameTemplate.CountRows(lengths));

    [Fact]
    public void Metadata_is_not_refused()
    {
        var plan = Valid([new FormField("start", "start", "start", null)], [], Dim("start", "x"));

        Assert.Equal("start", plan.Columns[0].Header);
    }
}
