﻿using System;
using System.Collections.Generic;

using CurlToSharp.Models;

using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.Extensions.Primitives;

namespace CurlToSharp.Services
{
    public class CommandLineParser : ICommandLineParser
    {
        private const char DoubleQuote = '"';

        private const char SingleQuote = '\'';

        private const char Space = ' ';

        public ParseResult<CurlOptions> Parse(Span<char> commandLine)
        {
            if (commandLine.IsEmpty)
            {
                throw new ArgumentException("The command line is empty.", nameof(commandLine));
            }

            Trim(ref commandLine);

            var parseResult = new ParseResult<CurlOptions>(new CurlOptions());
            var parseState = new ParseState();
            while (!commandLine.IsEmpty)
            {
                Trim(ref commandLine);
                if (commandLine.IsEmpty)
                {
                    break;
                }

                if (IsParameter(commandLine))
                {
                    var parameter = ReadParameter(ref commandLine);
                    EvaluateParameter(parameter, ref commandLine, parseResult);
                }
                else
                {
                    var value = ReadValue(ref commandLine);
                    EvaluateValue(parseResult, parseState, value);
                }
            }

            if (parseResult.Data.HttpMethod == null)
            {
                parseResult.Data.HttpMethod = HttpMethod.Get;
            }

            PostParsing(parseResult, parseState);

            return parseResult;
        }

        private static void EvaluateValue(ParseResult<CurlOptions> parseResult, ParseState parseState, Span<char> value)
        {
            var valueString = value.ToString();
            if (string.Equals(valueString, "curl", StringComparison.InvariantCultureIgnoreCase))
            {
                parseState.IsCurlCommand = true;
            }
            else if (parseResult.Data.Url == null && Uri.TryCreate(valueString, UriKind.Absolute, out var url))
            {
                parseResult.Data.Url = url;
            }
            else
            {
                parseState.LastUnknownValue = valueString;
            }
        }

        private void EvaluateParameter(Span<char> parameter, ref Span<char> commandLine, ParseResult<CurlOptions> parseResult)
        {
            string ReadValue(ref Span<char> span)
            {
                var value = this.ReadValue(ref span);

                return value.ToString();
            }

            var par = parameter.ToString();

            if (string.IsNullOrWhiteSpace(par))
            {
                return;
            }

            string val;
            switch (par)
            {
                    case "-X":
                    case "--request":
                        val = ReadValue(ref commandLine);
                        if (Enum.TryParse(val, true, out HttpMethod method))
                        {
                            parseResult.Data.HttpMethod = method;
                        }

                        break;
                    case "-d":
                    case "--data":
                        parseResult.Data.Payload = ReadValue(ref commandLine);
                        if (parseResult.Data.HttpMethod == null)
                        {
                            parseResult.Data.HttpMethod = HttpMethod.Post;
                        }

                        break;
                    case "-H":
                    case "--header":
                        val = ReadValue(ref commandLine);
                        if (!parseResult.Data.Headers.TryAdd(val.Split(":")[0].Trim(),  new StringValues(val.Split(":")[1].Trim())))
                        {
                            // Add error
                        }

                        break;
            }
        }

        private Span<char> ReadParameter(ref Span<char> commandLine)
        {
            Trim(ref commandLine);

            var indexOfSpace = commandLine.IndexOf(Space);
            if (indexOfSpace == -1)
            {
                indexOfSpace = commandLine.Length;
            }

            var parameter = commandLine.Slice(0, indexOfSpace);
            TrimQuotes(ref parameter);

            commandLine = commandLine.Slice(indexOfSpace);

            return parameter;
        }

        private Span<char> ReadValue(ref Span<char> commandLine)
        {
            Trim(ref commandLine);
            var firstChar = commandLine[0];
            int closeIndex;
            if ((firstChar == SingleQuote || firstChar == DoubleQuote) && commandLine.Length > 1)
            {
                var quote = firstChar;
                commandLine = commandLine.Slice(1);
                closeIndex = commandLine.IndexOf(quote) + 1;
            }
            else
            {
                closeIndex = commandLine.IndexOf(Space);
                if (closeIndex == -1)
                {
                    closeIndex = commandLine.Length;
                }
            }

            if (closeIndex == -1)
            {
                return Span<char>.Empty;
            }

            var value = commandLine.Slice(0, closeIndex);
            TrimQuotes(ref value);
            commandLine = commandLine.Slice(closeIndex);

            return value;
        }

        private void Trim(ref Span<char> input)
        {
            char space = ' ';
            int start;
            for (start = 0; start < input.Length; start++)
            {
                if (input[start] != space)
                {
                    break;
                }
            }

            int end;
            for (end = input.Length - 1; end > 0; end--)
            {
                if (input[end] != space)
                {
                    break;
                }
            }

            input = input.Slice(start, end + 1 - start);
        }

        private void TrimQuotes(ref Span<char> input)
        {
            int start;
            for (start = 0; start < input.Length; start++)
            {
                if (input[start] != DoubleQuote && input[start] != SingleQuote)
                {
                    break;
                }
            }

            int end;
            for (end = input.Length - 1; end > 0; end--)
            {
                if (input[end] != DoubleQuote && input[end] != SingleQuote)
                {
                    break;
                }
            }

            input = input.Slice(start, end + 1 - start);
        }

        private bool IsParameter(Span<char> commandLine)
        {
            return commandLine.IndexOf('-') == 0;
        }

        private void PostParsing(ParseResult<CurlOptions> result, ParseState state)
        {
            if (result.Data.Url == null
                && !string.IsNullOrWhiteSpace(state.LastUnknownValue)
                && Uri.TryCreate($"http://{state.LastUnknownValue}", UriKind.Absolute, out Uri url))
            {
                result.Data.Url = url;
            }

            if (!state.IsCurlCommand)
            {
                result.Errors.Add("Not a curl command");
            }

            if (result.Data.Url == null)
            {
                result.Errors.Add("Unable to parse URL");
            }
        }
    }
}