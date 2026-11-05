namespace SqlGlotSharp;

public static partial class helper
{
    // py: sqlglot/helper.py::logger #12092c5f48
    public static py.logging.Logger logger = py.logging.getLogger("sqlglot");

    /// <summary>Returns the value in `seq` at position `index`, or `None` if `index` is out of bounds.</summary>
    // py: sqlglot/helper.py::seq_get #0cf56cb359
    public static T? seq_get<T>(IReadOnlyList<T> seq, int index)
    {
        try
        {
            return seq[index < 0 ? seq.Count + index : index];
        }
        catch (ArgumentOutOfRangeException)
        {
            return default;
        }
        catch (IndexOutOfRangeException)
        {
            return default;
        }
    }
}
