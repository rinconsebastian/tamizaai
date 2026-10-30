using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Mcp;

/// <summary>A form and a published table the data tools read: select questions with their choice lists, numbers with a sentinel, dates, free text.</summary>
public static class McpDataFixtures
{
    public const string FieldsJson = """
        [{"name":"municipality","xpath":"municipality","type":"select_one","label":"Municipality","listName":"municipalities"},
         {"name":"water_source","xpath":"water_source","type":"select_one","label":"Main source of water","listName":"sources"},
         {"name":"uses","xpath":"uses","type":"select_multiple","label":"Uses of water","listName":"uses"},
         {"name":"age","xpath":"age","type":"integer","label":"Age"},
         {"name":"respondent_name","xpath":"respondent_name","type":"text","label":"Respondent name"}]
        """;

    public const string ChoicesJson = """
        [{"listName":"municipalities","name":"ana","label":"Santa Ana"},
         {"listName":"municipalities","name":"luis","label":"San Luis"},
         {"listName":"sources","name":"piped","label":"Piped"},
         {"listName":"sources","name":"well","label":"Well"},
         {"listName":"sources","name":"river","label":"River"},
         {"listName":"uses","name":"drink","label":"Drinking"},
         {"listName":"uses","name":"cook","label":"Cooking"},
         {"listName":"uses","name":"wash","label":"Washing"},
         {"listName":"uses","name":"irrigate","label":"Irrigation"}]
        """;

    public const string MainColumns =
        "municipality text, water_source text, uses text, age bigint, income numeric(10,2), visit_date date, visited_at timestamptz, has_water boolean, respondent_name text, sector text, note text";

    /// <summary>
    /// 60 rows: water_source Piped 36, Well 21, River 3; uses Drinking in 58 rows, Cooking in 30, Washing in 13, Irrigation in 2;
    /// ages 21 to 59 with one empty and four -99; a sector list of Rural 30, Urban 27, Island 3; 60 different names; 5 blank and 5 empty notes.
    /// </summary>
    public static string MainRows()
    {
        var rows = Enumerable.Range(1, 60).Select(i =>
        {
            var municipality = i <= 30 ? "Santa Ana" : "San Luis";
            var source = i <= 36 ? "Piped" : i <= 57 ? "Well" : "River";
            var uses = i <= 30 ? "Drinking; Cooking" : i <= 45 ? "Drinking" : i <= 58 ? "Washing; Drinking" : "Irrigation";
            var age = i <= 55 ? (20 + (i % 40)).ToString() : i == 56 ? "NULL" : "-99";
            var income = (i * 10.5m).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var date = new DateOnly(2026, 3, 1).AddDays(i).ToString("yyyy-MM-dd");
            var visited = $"{date} 10:00:00+00";
            var hasWater = i == 60 ? "NULL" : i % 5 != 0 ? "true" : "false";
            var sector = i <= 30 ? "Rural" : i <= 57 ? "Urban" : "Island";
            var note = i <= 5 ? "''" : i <= 10 ? "NULL" : "'x'";
            return $"('{municipality}','{source}','{uses}',{age},{income},'{date}','{visited}',{hasWater},'Person {i}','{sector}',{note})";
        });
        return string.Join(",", rows);
    }

    public static Task<Guid> PublishMainAsync(McpProjectHarness h, string step = "clean") =>
        PublishedTables.PublishAsync(h.Db, h.ProjectId, step, ("main", MainColumns, MainRows()));
}
