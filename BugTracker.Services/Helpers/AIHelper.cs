using BugTracker.Data;
using BugTracker.Data.Entities;
using BugTracker.Data.Models;
using DocumentFormat.OpenXml.Office2016.Excel;
using Microsoft.Extensions.Options;
using Mscc.GenerativeAI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BugTracker.Services.Helpers
{
    public interface IAIHelper
    {
        Task<string> GenerateOpenAiTestCases(string brdText);
        Task<object> GenerateGeminiTestCases(string brdText);
    }


    public class AIHelper : IAIHelper
    {
        //private readonly HttpClient _http;
        private readonly AppConfig _appConfig;
        private readonly Gemini _geminiCofig;
        //private readonly GenerativeModel _model;

        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _model;

        private const string SystemPrompt = "You are a helpful senior quality assurance software tester that is assigned to draft test case for projects";

        public AIHelper(
            HttpClient httpClient,
            IOptions<AppConfig> options,
            IOptions<Gemini> optionss
            )
        {
            //_http = factory.CreateClient("OpenAI");
            _appConfig = options.Value;
            _geminiCofig = optionss.Value;
            var googleAI = new GoogleAI(_geminiCofig.ApiKey);

            _httpClient = httpClient;
            _apiKey = _geminiCofig.ApiKey;
            _model = _geminiCofig.Model;
        }

        public async Task<string> GenerateOpenAiTestCases(string brdText)
        {
            var request = new
            {
                model = "gpt-4.1-mini",
                messages = new[]
                {
                    new { role = "system", content = "You are a senior QA engineer." },
                    new { role = "user", content = BuildPrompt(brdText) }
                },
                temperature = 0.2
            };

            var response = await _httpClient.PostAsJsonAsync("chat/completions", request);

            var result = await response.Content.ReadFromJsonAsync<dynamic>();

            return result.choices[0].message.content.ToString();
        }


        public async Task<object> GenerateGeminiTestCases(string brdText)
        {
            try
            {
                var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
                string userInput = BuildPrompt(brdText);
                var requestBody = new
                {
                    system_instruction = new
                    {
                        parts = new[] { new { text = SystemPrompt } }
                    },
                    contents = new[]
                    {
                        new
                        {
                            role = "user",
                            parts = new[] { new { text = userInput } }
                        }
                    }
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);
                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<JsonElement>(responseBody);

                // Extract the text from the response  
                var text = result
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

                if (string.IsNullOrWhiteSpace(text))
                    throw new Exception("Empty response from Gemini");

                var cleaned = text
                    .Replace("```json", "")
                    .Replace("```", "")
                    .Trim();

                // Extract valid JSON
                var start = cleaned.IndexOf('[');
                var end = cleaned.LastIndexOf(']');

                if (start >= 0 && end > start)
                {
                    cleaned = cleaned.Substring(start, end - start + 1);
                }

                var testCases = JsonSerializer.Deserialize<List<GeneratedTestCase>>(cleaned);

                return testCases;

            }
            catch (Exception ex)
            {
                return null;
            }

        }

        private string BuildPrompt(string brd)
        {
            return $@"
                Go through this document extensively and as a senior and experienced quality assurance tester, generate test cases from this BRD.
                Rules:
                - Return ONLY valid JSON.
                - Do NOT include explanations, text, or markdown.
                - Do NOT wrap in ```json.
                - Output MUST start with [ and end with ].
                - Ensure JSON is strictly valid and parsable.
                Format:
                [
                  {{
                    ""Title"": ""..."",
                    ""Description"": ""..."",
                    ""Preconditions"": ""..."",
                    ""Steps"": [
                      {{ ""Action"": ""..."", ""ExpectedOutcome"": ""..."" }}
                    ],
                    ""ExpectedResult"": ""..."",
                    ""Priority"": ""..."",
                    ""Tags"": [""..."", ""...""]
                  }}
                ]
                project description:
                {brd}";
        }

        //var prompt = $@"
        //    Generate software test cases for the following requirement:

        //    ""{request.InputText}""

        //    Return ONLY valid JSON in this format:
        //    {{
        //      ""testCases"": [
        //        {{
        //          ""id"": ""TC001"",
        //          ""description"": ""..."",
        //          ""steps"": [""step1"", ""step2""],
        //          ""expectedResult"": ""...""
        //        }}
        //      ]
        //    }}";

        //            var body = new
        //            {
        //                contents = new[]
        //                {
        //                    new
        //                    {
        //                        parts = new[]
        //                        {
        //                            new { text = prompt }
        //                        }
        //                    }
        //                }
        //            };

        //            var json = JsonSerializer.Serialize(body);
        //            var content = new StringContent(json, Encoding.UTF8, "application/json");

        //            var response = await _httpClient.PostAsync(
        //                $"https://generativelanguage.googleapis.com/v1/models/gemini-1.5-flash:generateContent?key={_apiKey}",
        //                content
        //            );

        //            var responseString = await response.Content.ReadAsStringAsync();

        //            using var doc = JsonDocument.Parse(responseString);

        //            var text = doc
        //                .RootElement
        //                .GetProperty("candidates")[0]
        //                .GetProperty("content")
        //                .GetProperty("parts")[0]
        //                .GetProperty("text")
        //                .GetString();

        //            // Clean + parse JSON safely
        //            try
        //            {
        //                var cleaned = text.Replace("```json", "").Replace("```", "").Trim();
        //        var parsed = JsonSerializer.Deserialize<object>(cleaned);

        //                return Ok(parsed);
        //    }
        //            catch
        //            {
        //        return StatusCode(500, new
        //        {
        //            error = "Invalid JSON from Gemini",
        //            raw = text
        //        });
        //    }
    }
}
