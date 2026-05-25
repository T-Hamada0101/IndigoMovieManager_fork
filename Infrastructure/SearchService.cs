using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace IndigoMovieManager.Infrastructure
{
    /// <summary>
    /// 検索文字列の解釈と各フィールド比較を 1 か所へ寄せる。
    /// UI はここを呼ぶだけにして、検索仕様の正本を本体へ固定する。
    /// </summary>
    public static class SearchService
    {
        public static bool IsDuplicateSearchKeyword(string searchKeyword)
        {
            if (string.IsNullOrWhiteSpace(searchKeyword))
            {
                return false;
            }

            string searchText = searchKeyword.Trim();
            if (!(searchText.StartsWith('{') && searchText.EndsWith('}')))
            {
                return false;
            }

            string inner = searchText[1..^1].Trim();
            return inner.Equals("dup", StringComparison.CurrentCultureIgnoreCase);
        }

        public static bool IsTagOnlySearchKeyword(string searchKeyword)
        {
            if (string.IsNullOrWhiteSpace(searchKeyword))
            {
                return false;
            }

            string searchText = searchKeyword.Trim();
            if (searchText.Equals("!notag", StringComparison.CurrentCultureIgnoreCase))
            {
                return true;
            }

            if (searchText.StartsWith('{') && searchText.EndsWith('}'))
            {
                string inner = searchText[1..^1].Trim();
                if (inner.Equals("notag", StringComparison.CurrentCultureIgnoreCase))
                {
                    return true;
                }
            }

            return TagSearchKeywordCodec.TryParsePureTagQuery(searchText, out _);
        }

        public static IEnumerable<MovieRecords> FilterMovies(
            IEnumerable<MovieRecords> source,
            string searchKeyword
        )
        {
            return FilterMovies(
                source,
                searchKeyword,
                CancellationToken.None,
                allowExpensiveAsciiPhoneticFallback: true
            );
        }

        public static IEnumerable<MovieRecords> FilterMovies(
            IEnumerable<MovieRecords> source,
            string searchKeyword,
            CancellationToken cancellationToken,
            bool allowExpensiveAsciiPhoneticFallback = true
        )
        {
            var query = WithCancellation(source ?? Enumerable.Empty<MovieRecords>(), cancellationToken);
            if (string.IsNullOrWhiteSpace(searchKeyword))
            {
                return query;
            }

            var searchText = searchKeyword.Trim();

            // exact tag 構文は通常検索と共存できるよう、先にタグ条件だけ抜き出す。
            query = ApplyExactTagFilters(
                query,
                searchText,
                cancellationToken,
                out string remainingSearchText
            );
            if (!ReferenceEquals(query, source) && string.IsNullOrWhiteSpace(remainingSearchText))
            {
                return query;
            }

            searchText = remainingSearchText;

            // 全体をクォートした時は、既存どおりフレーズ一致で扱う。
            if (TryGetQuotedPhrase(searchText, out string exact))
            {
                StringComparison comparison = ResolveSearchComparison(exact);
                bool useAsciiFastPath = comparison == StringComparison.OrdinalIgnoreCase;
                return WhereWithCancellation(query, item =>
                    ContainsInAnyField(
                        useAsciiFastPath
                            ? item.GetAsciiSearchFieldsForFilter(allowExpensiveAsciiPhoneticFallback)
                            : item.GetSearchFieldsForFilter(),
                        exact,
                        comparison
                    ),
                    cancellationToken
                );
            }

            // 既存の特殊コマンドはそのまま service 側へ寄せる。
            if (searchText.StartsWith('{') && searchText.EndsWith('}'))
            {
                var inner = searchText[1..^1].Trim();

                if (inner.Equals("notag", StringComparison.CurrentCultureIgnoreCase))
                {
                    return WhereWithCancellation(
                        query,
                        item => item.GetNormalizedTagsForFilter().Length == 0,
                        cancellationToken
                    );
                }

                if (inner.Equals("dup", StringComparison.CurrentCultureIgnoreCase))
                {
                    return FilterDuplicateMovies(query, cancellationToken);
                }
            }

            // 通常検索は OR -> AND -> NOT の順で既存仕様を保つ。
            SearchTerm[][] orGroups = CompileOrGroups(searchText);
            bool useAsciiSearchFieldFastPath = ShouldUseAsciiSearchFieldFastPath(orGroups);
            return WhereWithCancellation(query, item =>
            {
                string[] fields = useAsciiSearchFieldFastPath
                    ? item.GetAsciiSearchFieldsForFilter(allowExpensiveAsciiPhoneticFallback)
                    : item.GetSearchFieldsForFilter();

                return MatchesAnyOrGroup(fields, orGroups);
            },
            cancellationToken);
        }

        private static IEnumerable<MovieRecords> ApplyExactTagFilters(
            IEnumerable<MovieRecords> query,
            string searchText,
            CancellationToken cancellationToken,
            out string remainingSearchText
        )
        {
            remainingSearchText = searchText ?? "";

            if (searchText.Equals("!notag", StringComparison.CurrentCultureIgnoreCase))
            {
                remainingSearchText = "";
                return WhereWithCancellation(
                    query,
                    item => item.GetNormalizedTagsForFilter().Length == 0,
                    cancellationToken
                );
            }

            string[] tagKeywords = TagSearchKeywordCodec.ExtractActiveTags(searchText);
            if (tagKeywords.Length == 0)
            {
                return query;
            }

            remainingSearchText = TagSearchKeywordCodec.ReplaceTagFilters(searchText, Array.Empty<string>());
            return WhereWithCancellation(
                query,
                item => HasAllExactTags(item.GetNormalizedTagsForFilter(), tagKeywords),
                cancellationToken
            );
        }

        private static bool TryGetQuotedPhrase(string searchText, out string exact)
        {
            exact = string.Empty;
            if (searchText.Length < 2)
            {
                return false;
            }

            bool isDoubleQuoted = searchText.StartsWith('"') && searchText.EndsWith('"');
            bool isSingleQuoted = searchText.StartsWith('\'') && searchText.EndsWith('\'');
            if (!isDoubleQuoted && !isSingleQuoted)
            {
                return false;
            }

            exact = searchText[1..^1];
            return true;
        }

        private static SearchTerm[][] CompileOrGroups(string searchText)
        {
            string[] tokens = TagSearchKeywordCodec.TokenizeRemainingQuery(searchText);
            if (tokens.Length == 0)
            {
                return [];
            }

            List<SearchTerm[]> groups = [];
            List<SearchTerm> currentGroup = [];
            foreach (string token in tokens)
            {
                if (token == "|")
                {
                    if (currentGroup.Count > 0)
                    {
                        groups.Add(currentGroup.ToArray());
                        currentGroup.Clear();
                    }

                    continue;
                }

                currentGroup.Add(CompileTerm(token));
            }

            if (currentGroup.Count > 0)
            {
                groups.Add(currentGroup.ToArray());
            }

            return groups.Count == 0 ? [] : groups.ToArray();
        }

        private static SearchTerm CompileTerm(string token)
        {
            bool isNegative = token.StartsWith('-');
            string normalizedToken = isNegative ? token[1..] : token;
            bool isQuoted = TryGetQuotedPhrase(normalizedToken, out string exactTerm);

            return new SearchTerm(
                isQuoted ? exactTerm : normalizedToken,
                isNegative,
                isQuoted,
                ResolveSearchComparison(isQuoted ? exactTerm : normalizedToken)
            );
        }

        private static bool MatchesAnyOrGroup(string[] fields, SearchTerm[][] orGroups)
        {
            foreach (SearchTerm[] group in orGroups)
            {
                if (MatchesAllTerms(fields, group))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesAllTerms(string[] fields, SearchTerm[] group)
        {
            foreach (SearchTerm term in group)
            {
                if (string.IsNullOrWhiteSpace(term.Text))
                {
                    continue;
                }

                bool isMatched = term.IsNegative
                    ? ContainsInNoField(fields, term.Text, term.Comparison)
                    : ContainsInAnyField(fields, term.Text, term.Comparison);
                if (!isMatched)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ShouldUseAsciiSearchFieldFastPath(SearchTerm[][] orGroups)
        {
            foreach (SearchTerm[] group in orGroups)
            {
                foreach (SearchTerm term in group)
                {
                    if (term.Comparison != StringComparison.OrdinalIgnoreCase)
                    {
                        return false;
                    }
                }
            }

            return orGroups.Length > 0;
        }

        private static bool ContainsInAnyField(
            string[] fields,
            string text,
            StringComparison comparison
        )
        {
            foreach (string field in fields)
            {
                if (field.Contains(text, comparison))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsInNoField(
            string[] fields,
            string text,
            StringComparison comparison
        )
        {
            foreach (string field in fields)
            {
                if (field.Contains(text, comparison))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasAllExactTags(string[] movieTags, string[] requiredTags)
        {
            foreach (string requiredTag in requiredTags)
            {
                StringComparison comparison = ResolveSearchComparison(requiredTag);
                bool isMatched = false;
                foreach (string movieTag in movieTags)
                {
                    if (movieTag.Equals(requiredTag, comparison))
                    {
                        isMatched = true;
                        break;
                    }
                }

                if (!isMatched)
                {
                    return false;
                }
            }

            return true;
        }

        private static IEnumerable<MovieRecords> FilterDuplicateMovies(
            IEnumerable<MovieRecords> query,
            CancellationToken cancellationToken
        )
        {
            List<MovieRecords> materialized = query as List<MovieRecords>
                ?? MaterializeWithCancellation(query, cancellationToken);
            Dictionary<string, int> hashCounts = [];

            foreach (MovieRecords item in materialized)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(item?.Hash))
                {
                    continue;
                }

                hashCounts.TryGetValue(item.Hash, out int currentCount);
                hashCounts[item.Hash] = currentCount + 1;
            }

            return WhereWithCancellation(
                materialized,
                item =>
                    !string.IsNullOrEmpty(item?.Hash)
                    && hashCounts.TryGetValue(item.Hash, out int count)
                    && count > 1,
                cancellationToken
            );
        }

        private static List<MovieRecords> MaterializeWithCancellation(
            IEnumerable<MovieRecords> source,
            CancellationToken cancellationToken
        )
        {
            List<MovieRecords> items = [];
            foreach (MovieRecords item in source ?? Enumerable.Empty<MovieRecords>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                items.Add(item);
            }

            return items;
        }

        private static IEnumerable<MovieRecords> WithCancellation(
            IEnumerable<MovieRecords> source,
            CancellationToken cancellationToken
        )
        {
            foreach (MovieRecords item in source)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }
        }

        private static IEnumerable<MovieRecords> WhereWithCancellation(
            IEnumerable<MovieRecords> source,
            Func<MovieRecords, bool> predicate,
            CancellationToken cancellationToken
        )
        {
            foreach (MovieRecords item in source ?? Enumerable.Empty<MovieRecords>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (predicate(item))
                {
                    yield return item;
                }
            }
        }

        // ASCII だけの検索語は culture 比較より ordinal ignore case の方が軽い。
        // 日本語などを含む語は従来どおり culture 比較を維持して互換性を守る。
        private static StringComparison ResolveSearchComparison(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return StringComparison.CurrentCultureIgnoreCase;
            }

            foreach (char c in text)
            {
                if (c > sbyte.MaxValue)
                {
                    return StringComparison.CurrentCultureIgnoreCase;
                }
            }

            return StringComparison.OrdinalIgnoreCase;
        }

        private readonly record struct SearchTerm(
            string Text,
            bool IsNegative,
            bool IsQuoted,
            StringComparison Comparison
        );
    }
}
