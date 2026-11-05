namespace SqlGlotSharp.dialects;

/// <summary>TimescaleDB identity-only stage; no time_bucket/DateBin equivalence or transpilation mappings.</summary>
public static class timescale
{
    public class Timescale : postgres.Postgres;
}
