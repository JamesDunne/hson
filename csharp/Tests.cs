using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using WellDunne.Hson;

namespace csharp
{
    class Tests
    {
        static void Main(string[] args)
        {
            // Success cases:
            var testsMustSucceed = new string[] {
                @"",
                @"{}",
                @"/* hello world*/{}",
                @"// hello world
{}",
                @"{}/* hello world */",
                @"{}// hello world",
                @"{}
// word up!",
                @"/********/",
                @"{""key"": value}",
                @"{""key"": ""value""}",
                @"{""key"": 0.1423}",
                @"{""key"": []}",
                @"{""key"": [{},{}]}",
                @"{""key"": [,]}",                  // invalid JSON but passes parsing
                @"[]",
                @"[/* help! */1,2,3/*toomuch*/4]",  // Jury is out on what to do here... inject whitespace? It's still malformed JSON regardless.
                @"""word!""",
                @"@""multiline
test
here""",
                @"true",
                @"false",
                @"null",
                @"1.2",
                @"""abc\""word""",
                @"""a\u01C3bcd""",
            };
            
            // Failure cases:
            var testsMustFail = new string[] {
                @"/********",
                @"@""",
                @"""",
                @"""\",
                @"""\""",
                @"@""\",
                @"/+",
                @"/*",
                @"a / b"
            };

            // NOTE: HsonReader by default strips all whitespace from emitted JSON.

            // Test the success cases:
            Console.WriteLine("All these tests must succeed:");
            for (int i = 0; i < testsMustSucceed.Length; ++i)
                try
                {
                    using (var hr = new HsonReader(new MemoryStream(Encoding.UTF8.GetBytes(testsMustSucceed[i]))))
                    {
                        Console.WriteLine("'{0}'", hr.ReadToEnd());
                    }
                }
                catch (HsonParserException hpe)
                {
                    Console.WriteLine("UNEXPECTED FAIL: {0}", hpe.Message);
                }

            // Test the failure cases:
            Console.WriteLine();
            Console.WriteLine("All these tests must fail:");
            for (int i = 0; i < testsMustFail.Length; ++i)
                try
                {
                    using (var hr = new HsonReader(new MemoryStream(Encoding.UTF8.GetBytes(testsMustFail[i]))))
                    {
                        Console.WriteLine("UNEXPECTED PASS: '{0}'", hr.ReadToEnd());
                    }
                }
                catch (HsonParserException hpe)
                {
                    Console.WriteLine("EXPECTED FAIL: {0}", hpe.Message);
                }
        }
    }
}
