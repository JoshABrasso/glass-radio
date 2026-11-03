namespace GlassRadio.Application.Contracts;

// Persisted as a raw int in settings.json (no JsonStringEnumConverter configured anywhere in
// this solution) - member order must never be reshuffled without a settings migration (renaming
// a member in place, as Card -> Grid was, is safe: the stored int is unaffected).
// Grid: the Top Stations card look (square artwork, name/location below) applied to All Stations.
// List: the row layout (artwork thumbnail, name/location, favourite/play buttons) this app has
// used for All Stations since before this toggle existed - the default.
public enum AllStationsViewStyle
{
    Grid,
    List
}
