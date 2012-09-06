// HSON reader which strips out extended HSON features such as comments and multi-line
// string literals and emits raw JSON. Implemented as a StreamReader so that it may be
// injected into a chain of StreamReader and act as a filter.

/////////////////////////////////////////////
// LICENSE
/////////////////////////////////////////////
// Copyright (c) 2012, James S. Dunne
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met: 
// 
// 1. Redistributions of source code must retain the above copyright notice, this
//    list of conditions and the following disclaimer. 
// 2. Redistributions in binary form must reproduce the above copyright notice,
//    this list of conditions and the following disclaimer in the documentation
//    and/or other materials provided with the distribution. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
// ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
// WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
// ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
// (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
// LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
// ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
// SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
///////////////////////////////////////////////////////////////////////////////////

// I may be contacted via GitHub or via my contact link at http://bittwiddlers.org/

// https://github.com/JamesDunne/hson
// Feel free to submit pull requests to fix bugs or possibly add new features.

// This HSON format is intended *SOLELY* as a human-readable extension to JSON.
// It is *NOT* intended as a serialization format.
// DO NOT USE HSON AS A SERIALIZATION FORMAT.

// If you wish to serialize the JSON information contained within the HSON as part of 
// protocol, you must first use this HSON filter implementation to strip out all of the
// HSON extensions and produce raw (preferably minified) JSON output for serialization.
// Minification support is implemented in this class and is enabled by default.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WellDunne.Hson
{
    /// <summary>
    /// Determines how to emit whitespace in JSON.
    /// </summary>
    public enum JsonWhitespaceHandling
    {
        /// <summary>
        /// Removes all whitespace.
        /// </summary>
        NoWhitespace,
        /// <summary>
        /// Removes all whitespace but injects a single space character after a ':' or a ',' character.
        /// </summary>
        OnlySpacesAfterCommaAndColon,
        /// <summary>
        /// Leaves input HSON whitespace untouched, including extra whitespace found on comment-only lines.
        /// </summary>
        Untouched
    }

    /// <summary>
    /// Options to control the JSON emitter.
    /// </summary>
    public sealed class JsonEmitterOptions
    {
        /// <summary>
        /// Determines how whitespace is emitted. Default is NoWhitespace.
        /// </summary>
        public JsonWhitespaceHandling WhitespaceHandling { get; set; }
    }

    /// <summary>
    /// An HSON parser exception.
    /// </summary>
    public sealed class HsonParserException : Exception
    {
        internal HsonParserException(int line, int column, string messageFormat, params object[] args)
            : base(String.Format("HSON parser error at line {0}({1})", line, column) + ": " + String.Format(messageFormat, args))
        {
            Line = line;
            Column = column;
        }

        public int Line { get; private set; }
        public int Column { get; private set; }
    }

    /// <summary>
    /// This class reads in a stream assumed to be in HSON format (JSON with human-readable additions) and
    /// emits JSON as output to any Read() command. The output JSON is not guaranteed to be well-formed (see remarks).
    /// </summary>
    /// <remarks>
    /// The JSON subset of HSON is only superficially parsed to clean out comments and reparse multi-line string literals.
    /// </remarks>
    public sealed class HsonReader : StreamReader
    {
        readonly IEnumerator<char> hsonStripper;

        #region Constructors

        public HsonReader(Stream stream)
            : this(stream, Encoding.UTF8, true, 1024)
        {
        }

        public HsonReader(string path)
            : this(path, Encoding.UTF8, true, 1024)
        {
        }

        public HsonReader(Stream stream, bool detectEncodingFromByteOrderMarks)
            : this(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks, 1024)
        {
        }

        public HsonReader(Stream stream, Encoding encoding)
            : this(stream, encoding, true, 1024)
        {
        }

        public HsonReader(string path, bool detectEncodingFromByteOrderMarks)
            : this(path, Encoding.UTF8, detectEncodingFromByteOrderMarks, 1024)
        {
        }

        public HsonReader(string path, Encoding encoding)
            : this(path, encoding, true, 1024)
        {
        }

        public HsonReader(Stream stream, Encoding encoding, bool detectEncodingFromByteOrderMarks)
            : this(stream, encoding, detectEncodingFromByteOrderMarks, 1024)
        {
        }

        public HsonReader(string path, Encoding encoding, bool detectEncodingFromByteOrderMarks)
            : this(path, encoding, detectEncodingFromByteOrderMarks, 1024)
        {
        }

        public HsonReader(Stream stream, Encoding encoding, bool detectEncodingFromByteOrderMarks, int bufferSize)
            : base(stream, encoding, detectEncodingFromByteOrderMarks, bufferSize)
        {
            hsonStripper = StripHSON();
            EmitterOptions = new JsonEmitterOptions() { WhitespaceHandling = JsonWhitespaceHandling.NoWhitespace };
        }

        public HsonReader(string path, Encoding encoding, bool detectEncodingFromByteOrderMarks, int bufferSize)
            : base(path, encoding, detectEncodingFromByteOrderMarks, bufferSize)
        {
            hsonStripper = StripHSON();
            EmitterOptions = new JsonEmitterOptions() { WhitespaceHandling = JsonWhitespaceHandling.NoWhitespace };
        }

        #endregion

        #region Options

        /// <summary>
        /// Gets a mutable class that controls the JSON emitter options.
        /// </summary>
        public JsonEmitterOptions EmitterOptions { get; private set; }

        #endregion

        #region HSON parser

        delegate char EmitCharDelegate(char ch);
        delegate int ReadNextCharDelegate();

        /// <summary>
        /// This function parses HSON and emits JSON, but not necessarily well-formed JSON. The JSON subset of HSON is
        /// only superficially parsed to clean out comments and reparse multi-line string literals.
        /// </summary>
        /// <returns></returns>
        IEnumerator<char> StripHSON()
        {
            int line = 1, col = 1;
            char lastEmitted = (char)0;

            // Records the last-emitted character:
            EmitCharDelegate emit = delegate(char ec) { return lastEmitted = ec; };

            // Reads the next character and keeps track of current line/column:
            ReadNextCharDelegate readNext = delegate()
            {
                int x = base.Read();
                if (x == -1) return x;
                else if (x == '\r') return x;
                else if (x == '\n')
                {
                    ++line;
                    col = 1;
                    return x;
                }
                else ++col;
                return x;
            };

            int c, c2;

            // Read single chars at a time, relying on buffering implemented by base StreamReader class:
            c = readNext();
            while (c != -1)
            {
                // Parse comments and don't emit them:
                if (c == '/')
                {
                    c2 = readNext();
                    if (c2 == -1) throw new HsonParserException(line, col, "Unexpected end of stream");

                    if (c2 == '/')
                    {
                        // single line comment
                        c = readNext();
                        while (c != -1)
                        {
                            // Presence of an '\r' is irrelevant since we're not consuming it for storage.

                            // Stop at '\n':
                            if (c == '\n')
                            {
                                if (EmitterOptions.WhitespaceHandling == JsonWhitespaceHandling.Untouched)
                                {
                                    yield return emit((char)c);
                                }
                                c = readNext();
                                break;
                            }
                            else if (c == '\r')
                            {
                                if (EmitterOptions.WhitespaceHandling == JsonWhitespaceHandling.Untouched)
                                {
                                    yield return emit((char)c);
                                }
                                c = readNext();
                            }
                            else c = readNext();
                        }
                    }
                    else if (c2 == '*')
                    {
                        // block comment
                        c = readNext();
                        while (c != -1)
                        {
                            // Read up until '*/':
                            if (c == '*')
                            {
                                c = readNext();
                                if (c == -1) throw new HsonParserException(line, col, "Unexpected end of stream");
                                else if (c == '/') break;
                                else c = readNext();
                            }
                            else c = readNext();
                        }
                        if (c == -1) throw new HsonParserException(line, col, "Unexpected end of stream");
                        c = readNext();
                        continue;
                    }
                    // Not either comment type:
                    else throw new HsonParserException(line, col, "Unknown comment type");
                }
                else if (c == '@')
                {
                    // Parse the multiline string and emit a JSON string literal while doing so:
                    c = readNext();
                    if (c == -1) throw new HsonParserException(line, col, "Unexpected end of stream");
                    if (c != '"') throw new HsonParserException(line, col, "Malformed multi-line string literal");

                    if (EmitterOptions.WhitespaceHandling == JsonWhitespaceHandling.OnlySpacesAfterCommaAndColon)
                    {
                        if (lastEmitted == ':' || lastEmitted == ',') yield return ' ';
                    }

                    // Emit the opening '"':
                    yield return emit('"');

                    c = readNext();
                    if (c == -1) throw new HsonParserException(line, col, "Unexpected end of stream");
                    while (c != -1)
                    {
                        // Is it a terminating '"' or a double '""'?
                        if (c == '"')
                        {
                            c = readNext();
                            if (c == '"')
                            {
                                // Double quote chars are emitted as a single escaped quote char:
                                yield return emit('\\');
                                yield return emit('"');
                                c = readNext();
                            }
                            else
                            {
                                // Emit the terminating '"' and exit:
                                yield return emit('"');
                                break;
                            }
                        }
                        else if (c == '\\')
                        {
                            // Backslashes have no special meaning in multiline strings, pass them through as escaped:
                            yield return emit('\\');
                            yield return emit('\\');
                            c = readNext();
                        }
                        else if (c == '\r')
                        {
                            yield return emit('\\');
                            yield return emit('r');
                            c = readNext();
                        }
                        else if (c == '\n')
                        {
                            yield return emit('\\');
                            yield return emit('n');
                            c = readNext();
                        }
                        else
                        {
                            // Emit any other regular char:
                            yield return emit((char)c);
                            c = readNext();
                        }
                        if (c == -1) throw new HsonParserException(line, col, "Unexpected end of stream");
                    }
                }
                else if (c == '"')
                {
                    // Parse and emit the string literal:
                    if (EmitterOptions.WhitespaceHandling == JsonWhitespaceHandling.OnlySpacesAfterCommaAndColon)
                    {
                        if (lastEmitted == ':' || lastEmitted == ',') yield return ' ';
                    }
                    yield return emit((char)c);

                    c = readNext();
                    if (c == -1) throw new HsonParserException(line, col, "Unexpected end of stream");
                    while (c != -1)
                    {
                        if (c == '"')
                        {
                            // Yield the terminating '"' and exit:
                            yield return emit((char)c);
                            break;
                        }
                        else if (c == '\\')
                        {
                            // We don't care what escape sequence it is so long as we handle the '\"' case properly.
                            yield return emit((char)c);
                            c = readNext();
                            // An early-terminated escape sequence is an error:
                            if (c == -1) throw new HsonParserException(line, col, "Unexpected end of stream");
                            // Yield the escape char too:
                            yield return emit((char)c);
                            c = readNext();
                        }
                        else
                        {
                            yield return emit((char)c);
                            c = readNext();
                        }
                        if (c == -1) throw new HsonParserException(line, col, "Unexpected end of stream");
                    }

                    c = readNext();
                }
                // NOTE(jsd): We don't actually parse the underlying JSON, only recognize its basic tokens:
                else if (c == '{' || c == '[' || c == ',')
                {
                    if (EmitterOptions.WhitespaceHandling == JsonWhitespaceHandling.OnlySpacesAfterCommaAndColon)
                    {
                        if (lastEmitted == ':' || lastEmitted == ',') yield return ' ';
                    }
                    yield return emit((char)c);
                    c = readNext();
                }
                else if (c == ':' || c == ']' || c == '}')
                {
                    yield return emit((char)c);
                    c = readNext();
                }
                else if (Char.IsLetterOrDigit((char)c) || c == '_' || c == '.')
                {
                    if (EmitterOptions.WhitespaceHandling == JsonWhitespaceHandling.OnlySpacesAfterCommaAndColon)
                    {
                        if (lastEmitted == ':' || lastEmitted == ',') yield return ' ';
                    }
                    yield return emit((char)c);
                    c = readNext();
                }
                else if (Char.IsWhiteSpace((char)c))
                {
                    if (EmitterOptions.WhitespaceHandling == JsonWhitespaceHandling.Untouched)
                    {
                        yield return emit((char)c);
                    }
                    c = readNext();
                }
                else throw new HsonParserException(line, col, "Unexpected character '{0}'", (char)c);
            }
        }

        #endregion

        #region Public overrides

        public override int Read()
        {
            if (!hsonStripper.MoveNext()) return -1;
            return hsonStripper.Current;
        }

        public override int Peek()
        {
            return hsonStripper.Current;
        }

        public override int Read(char[] buffer, int index, int count)
        {
            if (count == 0) return 0;
            if (index >= buffer.Length) throw new ArgumentOutOfRangeException("index");
            if (count > buffer.Length) throw new ArgumentOutOfRangeException("count");
            if (index + count > buffer.Length) throw new ArgumentOutOfRangeException("count");

            int nr;
            for (nr = index; (nr < count) & hsonStripper.MoveNext(); ++nr)
            {
                buffer[nr] = hsonStripper.Current;
            }

            return nr;
        }

        public override string ReadLine()
        {
            StringBuilder sb = new StringBuilder();
            while (hsonStripper.MoveNext() & (hsonStripper.Current != '\n'))
            {
                sb.Append(hsonStripper.Current);
            }
            return sb.ToString();
        }

        public override string ReadToEnd()
        {
            StringBuilder sb = new StringBuilder();
            while (hsonStripper.MoveNext())
            {
                sb.Append(hsonStripper.Current);
            }
            return sb.ToString();
        }

        #endregion
    }
}
