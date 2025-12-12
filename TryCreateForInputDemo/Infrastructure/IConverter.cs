namespace GitConverter.Lib.Converters
{
    /// <summary>
    /// Represents a converter that transforms GIS data from one format to another.
    /// </summary>
    public interface IConverter
    {
        /// <summary>
        /// Gets the name of the converter (e.g., "GeoJson", "Shapefile", etc.)
        /// </summary>
        string Name { get; }
    }
}
