namespace GitConverter.Lib.Factories
{
    /// <summary>
    /// Factory interface for creating converter instances based on a converter key.
    /// </summary>
    public interface IConverterFactory
    {
        /// <summary>
        /// Attempts to create a converter instance for the specified converter key.
        /// </summary>
        /// <param name="converterKey">The key identifying the converter type (e.g., "GeoJson", "Shapefile").</param>
        /// <param name="converter">The created converter instance, or null if creation failed.</param>
        /// <returns>True if the converter was successfully created; otherwise, false.</returns>
        bool TryCreate(string converterKey, out Converters.IConverter converter);
    }
}
