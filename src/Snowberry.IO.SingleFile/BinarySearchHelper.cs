using System.IO.MemoryMappedFiles;

namespace Snowberry.IO.SingleFile;

public static class BinarySearchHelper
{
    public static unsafe int SearchInView(MemoryMappedViewAccessor accessor, byte[] toSearch)
    {
        var safeBuffer = accessor.SafeMemoryMappedViewHandle;
        byte* buffer = (byte*)safeBuffer.DangerousGetHandle();
        return SearchInBuffer(buffer, (int)accessor.Capacity, toSearch);
    }

    public static unsafe int SearchInBuffer(byte* buffer, int length, byte[] toSearch)
    {
        if (toSearch.Length > length)
            return -1;

        int[] table = BuildTable(toSearch);

        int i = 0;
        int j = 0;

        while (i + j < length)
        {
            if (toSearch[j] == buffer[i + j])
            {
                j++;

                if (j == toSearch.Length)
                {
                    return i;
                }
            }
            else
            {
                i += j - table[j];
                j = Math.Max(0, table[j]);
            }
        }

        return -1;
    }

    private static int[] BuildTable(byte[] toSearch)
    {
        int[] table = new int[toSearch.Length];
        int position = 2;
        int candidate = 0;

        table[0] = -1;
        table[1] = 0;

        while (position < toSearch.Length)
        {
            if (toSearch[position - 1] == toSearch[candidate])
            {
                table[position++] = ++candidate;
                continue;
            }

            if (candidate > 0)
            {
                candidate = table[candidate];
                continue;
            }

            table[position++] = 0;
        }

        return table;
    }
}

