namespace TS3Modding.API
{
    // Transient tow cleanup, not completion evidence for a scheduled departure.
    public interface IDormantTowStripArchivalService
    {
        bool TryArchiveDormantTowStrip(StripSnapshot expected, object nativeAircraft, object expectedSchedule);
    }
}
