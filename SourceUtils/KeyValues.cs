﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Facepunch.Parse;
using SourceUtils.Properties;

namespace SourceUtils
{
    [Flags]
    public enum KeyValuesFlags
    {
        Default = 0,
        UsesEscapeSequences = 1
    }

    public class KeyValuesParserException : Exception
    {
        public ParseResult ParseResult { get; }

        public KeyValuesParserException( ParseResult parseResult )
            : base( parseResult.ErrorMessage )
        {
            ParseResult = parseResult;
        }
    }

    public class KeyValues
    {
        public class Entry : IEnumerable<string>
        {
            public static bool operator==( Entry a, object b )
            {
                return ReferenceEquals( a, b ) || b == null && a.IsNull;
            }

            public static bool operator !=( Entry a, object b )
            {
                return !(a == b);
            }

            public static implicit operator string( Entry entry )
            {
                return entry == null || !entry.HasValue ? null : entry._values.LastOrDefault();
            }

            public static implicit operator bool( Entry entry )
            {
                return (int) entry != 0;
            }

            public static implicit operator int( Entry entry )
            {
                return int.TryParse( entry, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result ) ? result : 0;
            }

            public static implicit operator float( Entry entry )
            {
                return float.TryParse( entry, NumberStyles.Float, CultureInfo.InvariantCulture, out var result ) ? result : 0f;
            }

            private static readonly Regex _sColorRegex = new Regex( @"^\s*\{\s*(?<red>[0-9]+)\s+(?<green>[0-9]+)\s+(?<blue>[0-9]+)\s*\}\s*$" );

            public static implicit operator Color32( Entry entry )
            {
                var value = (string) entry;
                var match = _sColorRegex.Match( value );

                if ( !match.Success )
                {
                    return new Color32( 0xff, 0x00, 0xff );
                }

                return new Color32
                {
                    R = (byte) int.Parse( match.Groups["red"].Value ),
                    G = (byte) int.Parse( match.Groups["green"].Value ),
                    B = (byte) int.Parse( match.Groups["blue"].Value ),
                    A = 255
                };
            }

            public static Entry Null { get; } = new Entry();

            private Dictionary<string, Entry> _subEntries;
            private List<string> _values;

            public Entry this[string key] => _subEntries != null && _subEntries.TryGetValue( key, out var entry ) ? entry : Null;

            public bool IsNull => !HasValue && !HasKeys;
            
            public bool HasValue => _values != null && _values.Count > 0;
            public bool HasKeys => _subEntries != null && _subEntries.Count > 0;

            public string Value => HasValue ? _values[_values.Count - 1] : null;

            public IEnumerable<string> Keys => _subEntries == null ? Enumerable.Empty<string>() : _subEntries.Keys;

            internal Entry() { }

            internal void AddValue( ParseResult result, KeyValuesFlags flags )
            {
                if ( result.Parser.ElementName.EndsWith( ".String" ) )
                {
                    if ( _values == null ) _values = new List<string>();

                    _values.Add( ReadString( result, flags ) );
                    return;
                }

                AssertParser( result, ".Definition.List" );

                if ( result.Length == 0 ) return;

                if ( _subEntries == null ) _subEntries = new Dictionary<string, Entry>( StringComparer.InvariantCultureIgnoreCase );
                
                foreach ( var def in result )
                {
                    var key = ReadString( def[0], flags );

                    if ( !_subEntries.TryGetValue( key, out var existing ) )
                    {
                        _subEntries.Add( key, existing = new Entry() );
                    }

                    existing.AddValue( def[1], flags );
                }
            }

            public bool ContainsKey( string key )
            {
                return HasKeys && _subEntries.ContainsKey( key );
            }

            public void MergeFrom( Entry other, bool replace )
            {
                if ( !other.HasKeys ) return;

                foreach ( var key in other.Keys )
                {
                    if ( _subEntries.ContainsKey( key ) )
                    {
                        if ( replace ) _subEntries[key] = other[key];
                    }
                    else
                    {
                        _subEntries.Add( key, other[key] );
                    }
                }
            }

            public IEnumerator<string> GetEnumerator()
            {
                return _values == null ? Enumerable.Empty<string>().GetEnumerator() : _values.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        private static readonly Parser _sEscapedParser;
        private static readonly Parser _sUnescapedParser;

        static KeyValues()
        {
            var grammar = GrammarBuilder.FromString( Resources.KeyValues );

            _sEscapedParser = grammar["Escaped.Document"];
            _sUnescapedParser = grammar["Unescaped.Document"];
        }

        [Conditional( "DEBUG" )]
        private static void AssertParser( ParseResult result, string name )
        {
            Debug.Assert( result.Parser.ElementName.EndsWith( name ) );
        }

        [ThreadStatic]
        private static StringBuilder _sBuilder;

        private static string ReadString( ParseResult result, KeyValuesFlags flags )
        {
            AssertParser( result, ".String" );
            
            var rawValue = result[0].Value;

            if ( (flags & KeyValuesFlags.UsesEscapeSequences) != 0 )
            {
                var builder = _sBuilder ?? (_sBuilder = new StringBuilder());
                builder.Remove( 0, builder.Length );

                for ( var i = 0; i < rawValue.Length; ++i )
                {
                    var c = rawValue[i];
                    switch ( c )
                    {
                        case '\\':
                            switch ( c = rawValue[++i] )
                            {
                                case 'n':
                                    builder.Append( '\n' );
                                    break;
                                case 't':
                                    builder.Append( '\t' );
                                    break;
                                case '"':
                                case '\\':
                                    builder.Append( c );
                                    break;
                            }
                            break;
                        default:
                            builder.Append( c );
                            break;
                    }
                }

                return builder.ToString();
            }

            return rawValue;
        }

        public static bool TryParse( string value, out KeyValues result, KeyValuesFlags flags = KeyValuesFlags.Default )
        {
            try
            {
                result = Parse( value, flags );
                return true;
            }
            catch
            {
                result = null;
                return false;
            }
        }

        public static KeyValues Parse( string value, KeyValuesFlags flags = KeyValuesFlags.Default )
        {
            var parser = (flags & KeyValuesFlags.UsesEscapeSequences) != 0 ? _sEscapedParser : _sUnescapedParser;
            var result = parser.Parse( value );

            if ( !result.Success )
            {
                throw new KeyValuesParserException( result );
            }

            return new KeyValues( result, flags );
        }

        public static KeyValues FromStream( Stream stream, KeyValuesFlags flags = KeyValuesFlags.Default )
        {
            using ( var reader = new StreamReader( stream ) )
            {
                return Parse( reader.ReadToEnd(), flags );
            }
        }

        private readonly Entry _root;

        public IEnumerable<string> Keys => _root.Keys;

        private KeyValues( ParseResult result, KeyValuesFlags flags )
        {
            AssertParser( result, ".Document" );

            _root = new Entry();
            _root.AddValue( result[0], flags );
        }

        public bool ContainsKey( string key )
        {
            return _root.ContainsKey( key );
        }

        public Entry this[string key] => _root[key];
    }
}