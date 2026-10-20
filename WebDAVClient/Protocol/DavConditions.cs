#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;

namespace WebDAVClient.Protocol
{
    /// <summary>
    /// One state-token or entity-tag condition, optionally negated.
    /// </summary>
    public sealed class DavCondition
    {
        private DavCondition(string wire)
        {
            Wire = wire;
        }

        internal string Wire
        {
            get;
        }

        /// <summary>
        /// Matches an opaque absolute state-token URI; pass the bare token.
        /// </summary>
        public static DavCondition StateToken(string token, bool negated = false)
        {
            ValidateToken(token);
            return new DavCondition((negated ? "Not " : "") + "<" + token + ">");
        }

        /// <summary>
        /// Matches an entity tag using DAV If semantics.
        /// </summary>
        public static DavCondition EntityTag(string etag, bool negated = false)
        {
            if (etag == "*")
            {
                throw new ArgumentException("DAV If requires an entity tag, not '*'.", nameof(etag));
            }
            var parsed = EntityTagHeaderValue.Parse(etag);
            return new DavCondition((negated ? "Not " : "") + "[" + parsed + "]");
        }

        internal static void ValidateToken(string token)
        {
            ArgumentNullException.ThrowIfNull(token);
            if (token.Any(char.IsWhiteSpace) || token.IndexOfAny(new[]
                {
                    '<', '>', '\r', '\n'
                }) >= 0
                || !Uri.TryCreate(token, UriKind.Absolute, out _))
            {
                throw new ArgumentException("A state token must be a bare absolute URI.", nameof(token));
            }
        }
    }

    /// <summary>
    /// An AND-list of conditions. Multiple lists are alternatives, not a combined lock assertion.
    /// </summary>
    public sealed class DavConditionList
    {
        /// <summary>
        /// Snapshots a nonempty set of conditions.
        /// </summary>
        public DavConditionList(params DavCondition[] conditions)
        {
            ArgumentNullException.ThrowIfNull(conditions);
            if (conditions.Length == 0 || conditions.Any(c => c == null))
            {
                throw new ArgumentException("At least one non-null condition is required.", nameof(conditions));
            }
            Conditions = Array.AsReadOnly(conditions.ToArray());
        }

        /// <summary>
        /// Conditions evaluated together.
        /// </summary>
        public IReadOnlyList<DavCondition> Conditions
        {
            get;
        }

        internal string ToHeader() => "(" + string.Join(" ", Conditions.Select(c => c.Wire)) + ")";
    }

    /// <summary>
    /// Alternative condition lists tagged with one resource URI.
    /// </summary>
    public sealed class DavTaggedConditions
    {
        /// <summary>
        /// Snapshots resource-associated alternatives.
        /// </summary>
        public DavTaggedConditions(DavPath resource, params DavConditionList[] alternatives)
        {
            ArgumentNullException.ThrowIfNull(resource);
            Resource = resource;
            Alternatives = DavIfConditions.CopyLists(alternatives);
        }

        /// <summary>
        /// Resource to which the lists apply; validated against the client origin.
        /// </summary>
        public DavPath Resource
        {
            get;
        }

        /// <summary>
        /// Alternative AND-lists.
        /// </summary>
        public IReadOnlyList<DavConditionList> Alternatives
        {
            get;
        }
    }

    /// <summary>
    /// A complete tagged or untagged DAV If expression; the two forms cannot be mixed.
    /// </summary>
    public sealed class DavIfConditions
    {
        private readonly IReadOnlyList<DavConditionList>? m_untagged;

        private readonly IReadOnlyList<DavTaggedConditions>? m_tagged;

        private DavIfConditions(IReadOnlyList<DavConditionList>? untagged, IReadOnlyList<DavTaggedConditions>? tagged)
        {
            m_untagged = untagged;
            m_tagged = tagged;
        }

        /// <summary>
        /// Creates alternatives for the request URI.
        /// </summary>
        public static DavIfConditions Untagged(params DavConditionList[] alternatives) => new DavIfConditions(CopyLists(alternatives), null);

        /// <summary>
        /// Creates tagged alternatives for multiple resources.
        /// </summary>
        public static DavIfConditions Tagged(params DavTaggedConditions[] resources)
        {
            ArgumentNullException.ThrowIfNull(resources);
            if (resources.Length == 0 || resources.Any(r => r == null))
            {
                throw new ArgumentException("At least one non-null resource is required.", nameof(resources));
            }
            return new DavIfConditions(null, Array.AsReadOnly(resources.ToArray()));
        }

        internal static IReadOnlyList<DavConditionList> CopyLists(DavConditionList[] alternatives)
        {
            ArgumentNullException.ThrowIfNull(alternatives);
            if (alternatives.Length == 0 || alternatives.Any(a => a == null))
            {
                throw new ArgumentException("At least one non-null alternative is required.", nameof(alternatives));
            }
            return Array.AsReadOnly(alternatives.ToArray());
        }

        internal string ToHeader(Func<DavPath, Uri> resolve) => m_untagged != null
            ? string.Join(" ", m_untagged.Select(a => a.ToHeader()))
            : string.Join(" ", m_tagged!.Select(r => "<" + resolve(r.Resource).AbsoluteUri + "> " + string.Join(" ", r.Alternatives.Select(a => a.ToHeader()))));
    }
}
