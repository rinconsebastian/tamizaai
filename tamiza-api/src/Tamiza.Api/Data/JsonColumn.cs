using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Tamiza.Api.Data;

/// <summary>Maps a list property to a <c>jsonb</c> column, compared by content for change tracking.</summary>
internal static class JsonColumn
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static PropertyBuilder<List<T>> HasJsonColumn<T>(this PropertyBuilder<List<T>> property)
    {
        var converter = new ValueConverter<List<T>, string>(
            value => JsonSerializer.Serialize(value, Options),
            json => JsonSerializer.Deserialize<List<T>>(json, Options) ?? new List<T>());
        var comparer = new ValueComparer<List<T>>(
            (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
            value => JsonSerializer.Serialize(value, Options).GetHashCode(),
            value => JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(value, Options), Options)!);
        return property.HasConversion(converter, comparer).HasColumnType("jsonb");
    }

    /// <summary>Maps an object property to a <c>jsonb</c> column, compared by content for change tracking.</summary>
    public static PropertyBuilder<T> HasJsonObjectColumn<T>(this PropertyBuilder<T> property)
        where T : class?
    {
        var converter = new ValueConverter<T, string>(
            value => JsonSerializer.Serialize(value, Options),
            json => JsonSerializer.Deserialize<T>(json, Options)!);
        var comparer = new ValueComparer<T>(
            (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
            value => JsonSerializer.Serialize(value, Options).GetHashCode(),
            value => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!);
        return property.HasConversion(converter, comparer).HasColumnType("jsonb");
    }
}
