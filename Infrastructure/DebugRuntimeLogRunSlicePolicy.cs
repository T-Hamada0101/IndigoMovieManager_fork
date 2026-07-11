using System;
using System.Collections.Generic;

namespace IndigoMovieManager.Infrastructure
{
    // DetectedResetCountは、先頭runより後で検出したsequence=1の境界数を表す。
    public readonly record struct DebugRuntimeLogRunSliceResult(
        IReadOnlyList<string> Lines,
        bool HasSequence,
        long? StartSequence,
        long? EndSequence,
        int DetectedResetCount,
        int SourceLineCount
    )
    {
        public string BuildSummaryText()
        {
            string sequenceText =
                HasSequence && StartSequence.HasValue && EndSequence.HasValue
                    ? $"{StartSequence.Value}-{EndSequence.Value}"
                    : "none";

            return string.Join(
                " ",
                $"log_run_lines={Lines.Count}/{SourceLineCount}",
                $"has_sequence={(HasSequence ? "true" : "false")}",
                $"sequence={sequenceText}",
                $"resets={DetectedResetCount}"
            );
        }
    }

    public static class DebugRuntimeLogRunSlicePolicy
    {
        public static DebugRuntimeLogRunSliceResult SliceLatestRun(IEnumerable<string> sourceLines)
        {
            if (sourceLines is null)
            {
                return Empty(sourceLineCount: 0);
            }

            List<string> currentRunLines = new();
            int sourceLineCount = 0;
            int detectedResetCount = 0;
            bool currentRunHasSequence = false;
            long? startSequence = null;
            long? endSequence = null;

            foreach (string sourceLine in sourceLines)
            {
                sourceLineCount++;
                string line = sourceLine ?? "";
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (TryReadSequence(line, out long sequence))
                {
                    // 起動runは必ず1から始まる。並列追記による途中の逆転や重複は境界にしない。
                    if (currentRunHasSequence && sequence == 1)
                    {
                        detectedResetCount++;
                        currentRunLines.Clear();
                        currentRunHasSequence = false;
                        startSequence = null;
                        endSequence = null;
                    }

                    if (!currentRunHasSequence)
                    {
                        startSequence = sequence;
                        currentRunHasSequence = true;
                    }

                    endSequence = sequence;
                }

                currentRunLines.Add(line);
            }

            if (currentRunLines.Count == 0)
            {
                return Empty(sourceLineCount);
            }

            return new DebugRuntimeLogRunSliceResult(
                currentRunLines,
                currentRunHasSequence,
                startSequence,
                endSequence,
                detectedResetCount,
                sourceLineCount
            );
        }

        private static DebugRuntimeLogRunSliceResult Empty(int sourceLineCount)
        {
            return new DebugRuntimeLogRunSliceResult(
                Array.Empty<string>(),
                HasSequence: false,
                StartSequence: null,
                EndSequence: null,
                DetectedResetCount: 0,
                sourceLineCount
            );
        }

        private static bool TryReadSequence(string line, out long sequence)
        {
            sequence = 0;
            int hashIndex = line.IndexOf('#');
            if (hashIndex < 0 || hashIndex + 1 >= line.Length)
            {
                return false;
            }

            int digitStart = hashIndex + 1;
            int digitEnd = digitStart;
            while (digitEnd < line.Length && char.IsDigit(line[digitEnd]))
            {
                digitEnd++;
            }

            if (digitEnd == digitStart)
            {
                return false;
            }

            ReadOnlySpan<char> digits = line.AsSpan(digitStart, digitEnd - digitStart);
            return long.TryParse(digits, out sequence);
        }
    }
}
