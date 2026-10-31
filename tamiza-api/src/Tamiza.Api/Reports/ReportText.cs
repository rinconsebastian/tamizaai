using System.Globalization;

namespace Tamiza.Api.Reports;

/// <summary>
/// The words a scheduled email writes itself, in English and Spanish (design D8). Names, labels and data values are never translated.
/// Several texts are those of the UI's translation files, so the email and the dashboard say the same: keep them in step.
/// </summary>
public sealed partial class ReportText
{
    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
    {
        ["summary"] = "Summary",
        ["project"] = "Project",
        ["dashboard"] = "Dashboard",
        ["generated"] = "Generated",
        ["dataPublished"] = "Data published",
        ["dataNotPublished"] = "No indicator of this dashboard has published yet",
        ["filters"] = "Filters",
        ["noFilters"] = "No filter applies",
        ["indicators"] = "Indicators",
        ["widget"] = "Widget",
        ["value"] = "Value",
        ["notes"] = "Notes",
        ["moreRows.one"] = "1 more row",
        ["moreRows.other"] = "{0} more rows",
        ["attached"] = "The full tables are in the attached workbook.",
        ["tooLarge"] = "The workbook was too large to attach. Members can export the indicator tables from the dashboard.",
        ["notShown"] = "To keep this email short, these widgets are not shown in its body:",
        ["footer.scheduled"] = "A member of {0} scheduled this email from Tamiza.",
        ["footer.stop"] = "To stop receiving it, reply to this email.",
        ["footer.open"] = "Members can open the dashboard at {0}",
        ["subject"] = "{0} – {1} – {2}",
        // As in tamiza-ui's en.json.
        ["notPublished"] = "{0} has not published yet.",
        ["missingColumns"] = "The published table no longer has the columns: {0}.",
        ["noLongerFits"] = "This widget's settings no longer fit its indicator's table.",
        ["timedOut"] = "This widget could not be read in time.",
        ["empty"] = "No rows match this widget’s query.",
        ["rowLimit"] = "Only the first {0} rows are shown: the query matches more.",
        ["notFiltered"] = "Not filtered by: {0}",
        ["noGroup"] = "(none)",
        ["range"] = "Range",
        ["rowsOf"] = "Rows of {0}",
        ["share"] = "Share",
        ["total"] = "Total",
        ["other"] = "Other",
        ["moreColumns.one"] = "1 more column",
        ["moreColumns.other"] = "{0} more columns",
        ["leftOut.one"] = "1 row was left out: its group or value is not one the chart shows.",
        ["leftOut.other"] = "{0} rows were left out: their group or value is not one the chart shows.",
        ["negativeLeftOut.one"] = "1 negative value was left out of the shares.",
        ["negativeLeftOut.other"] = "{0} negative values were left out of the shares.",
        ["yes"] = "Yes",
        ["no"] = "No",
        ["true"] = "true",
        ["false"] = "false",
        ["emptyText"] = "(empty text)",
        ["range.both"] = "{0} to {1}",
        ["range.min"] = "from {0}",
        ["range.max"] = "up to {0}",
    };

    private static readonly IReadOnlyDictionary<string, string> Spanish = new Dictionary<string, string>
    {
        ["summary"] = "Resumen",
        ["project"] = "Proyecto",
        ["dashboard"] = "Tablero",
        ["generated"] = "Generado",
        ["dataPublished"] = "Datos publicados",
        ["dataNotPublished"] = "Ningún indicador de este tablero se ha publicado aún",
        ["filters"] = "Filtros",
        ["noFilters"] = "No se aplica ningún filtro",
        ["indicators"] = "Indicadores",
        ["widget"] = "Widget",
        ["value"] = "Valor",
        ["notes"] = "Notas",
        ["moreRows.one"] = "1 fila más",
        ["moreRows.other"] = "{0} filas más",
        ["attached"] = "Las tablas completas están en el libro de Excel adjunto.",
        ["tooLarge"] = "El libro de Excel era demasiado grande para adjuntarlo. Los miembros pueden exportar las tablas de los indicadores desde el tablero.",
        ["notShown"] = "Para que este correo no sea demasiado largo, estos widgets no se muestran en su cuerpo:",
        ["footer.scheduled"] = "Un miembro de {0} programó este correo desde Tamiza.",
        ["footer.stop"] = "Para dejar de recibirlo, responde a este correo.",
        ["footer.open"] = "Los miembros pueden abrir el tablero en {0}",
        ["subject"] = "{0} – {1} – {2}",
        // As in tamiza-ui's es.json.
        ["notPublished"] = "{0} aún no se ha publicado.",
        ["missingColumns"] = "La tabla publicada ya no tiene las columnas: {0}.",
        ["noLongerFits"] = "La configuración de este widget ya no corresponde a la tabla de su indicador.",
        ["timedOut"] = "Este widget no se pudo leer a tiempo.",
        ["empty"] = "Ninguna fila coincide con la consulta de este widget.",
        ["rowLimit"] = "Solo se muestran las primeras {0} filas: la consulta coincide con más.",
        ["notFiltered"] = "Sin filtrar por: {0}",
        ["noGroup"] = "(ninguno)",
        ["range"] = "Rango",
        ["rowsOf"] = "Filas de {0}",
        ["share"] = "Proporción",
        ["total"] = "Total",
        ["other"] = "Otros",
        ["moreColumns.one"] = "1 columna más",
        ["moreColumns.other"] = "{0} columnas más",
        ["leftOut.one"] = "Se dejó fuera 1 fila: su grupo o su valor no es de los que muestra el gráfico.",
        ["leftOut.other"] = "Se dejaron fuera {0} filas: su grupo o su valor no es de los que muestra el gráfico.",
        ["negativeLeftOut.one"] = "Se dejó fuera de las proporciones 1 valor negativo.",
        ["negativeLeftOut.other"] = "Se dejaron fuera de las proporciones {0} valores negativos.",
        ["yes"] = "Sí",
        ["no"] = "No",
        ["true"] = "verdadero",
        ["false"] = "falso",
        ["emptyText"] = "(texto vacío)",
        ["range.both"] = "{0} a {1}",
        ["range.min"] = "desde {0}",
        ["range.max"] = "hasta {0}",
    };

    private readonly IReadOnlyDictionary<string, string> _texts;

    private ReportText(IReadOnlyDictionary<string, string> texts, CultureInfo culture)
    {
        _texts = texts;
        Culture = culture;
    }

    public CultureInfo Culture { get; }

    /// <summary>The keys of each language, so a test can check they are the same.</summary>
    public static (IReadOnlyCollection<string> English, IReadOnlyCollection<string> Spanish) Keys => ([.. English.Keys], [.. Spanish.Keys]);

    /// <summary>
    /// Whether <paramref name="locale"/> resolves to Spanish or English regional formats (see <see cref="Resolve"/>): the UI is
    /// shown in those (see <c>interface-language</c>).
    /// </summary>
    public static bool IsSupported(string? locale) => Resolve(locale) is not null;

    /// <summary>
    /// The culture name to store for the UI's <paramref name="locale"/>: the same name when the server has regional data for it, such
    /// as <c>es-CO</c> or <c>en-GB</c>; its language's name when it has none, as for <c>en-CO</c> or <c>es-DE</c>, which browsers send
    /// for a language set with a device's region. Null for anything that is not Spanish or English, or not a culture name.
    /// </summary>
    public static string? Resolve(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale) || locale.Length > 35)
        {
            return null;
        }

        try
        {
            return CultureInfo.GetCultureInfo(locale, predefinedOnly: true) is { TwoLetterISOLanguageName: "es" or "en" } known ? known.Name : null;
        }
        catch (CultureNotFoundException)
        {
            // A well-formed tag the server has no data for: its language's formats, when that is Spanish or English.
            var language = locale.Split('-', '_')[0].ToLowerInvariant();
            return language is "es" or "en" && LanguageTag().IsMatch(locale) ? language : null;
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z]{2,3}([-_][A-Za-z0-9]{2,8})*$")]
    private static partial System.Text.RegularExpressions.Regex LanguageTag();

    /// <summary>The texts and regional formats of a supported locale.</summary>
    public static ReportText For(string locale)
    {
        var culture = CultureInfo.GetCultureInfo(locale, predefinedOnly: true);
        return new ReportText(culture.TwoLetterISOLanguageName == "es" ? Spanish : English, culture);
    }

    public string this[string key] => _texts[key];

    public string Format(string key, params object?[] values) => string.Format(Culture, _texts[key], values);

    /// <summary>"1 more row" or "22 more rows": both languages have only these two forms.</summary>
    public string MoreRows(int count) => count == 1 ? this["moreRows.one"] : Format("moreRows.other", count);

    /// <summary>"1 more column" or "19 more columns".</summary>
    public string MoreColumns(int count) => count == 1 ? this["moreColumns.one"] : Format("moreColumns.other", count);

    /// <summary>The note of a pyramid or a Likert widget that left rows out.</summary>
    public string LeftOut(long count) => count == 1 ? this["leftOut.one"] : Format("leftOut.other", count);

    /// <summary>The note of a widget that shows shares and set negative cells aside.</summary>
    public string NegativeLeftOut(int count) => count == 1 ? this["negativeLeftOut.one"] : Format("negativeLeftOut.other", count);
}
