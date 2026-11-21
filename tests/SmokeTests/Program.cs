using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using var client = new HttpClient { BaseAddress = new Uri(args.FirstOrDefault() ?? "http://127.0.0.1:8000"), Timeout = TimeSpan.FromSeconds(10) };
void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
async Task<(int Status, JsonElement Json)> Read(HttpResponseMessage response) {
    using (response) { var text = await response.Content.ReadAsStringAsync(); return ((int)response.StatusCode, JsonSerializer.Deserialize<JsonElement>(text)); }
}
var code = await Read(await client.GetAsync("/api/getCode?uid=test-user-001"));
Check(code.Status == 200 && code.Json.GetProperty("ret_code").GetInt32() == 0, "Generate Code");
var token = await Read(await client.PostAsJsonAsync("/api/getSSToken", new { code = code.Json.GetProperty("data").GetProperty("code").GetString() }));
Check(token.Status == 200 && token.Json.GetProperty("data").GetProperty("user_info").GetProperty("uid").GetString() == "test-user-001", "Exchange Code and read UID");
await Task.Delay(1100);
var refreshed = await Read(await client.PostAsJsonAsync("/api/updateSSToken", new { ss_token = token.Json.GetProperty("data").GetProperty("ss_token").GetString() }));
Check(refreshed.Status == 200 && refreshed.Json.GetProperty("data").GetProperty("expire_date").GetDecimal() > token.Json.GetProperty("data").GetProperty("expire_date").GetDecimal(), "Refresh SSToken");
foreach (var pair in new[] { ("/api/getSSToken", "code"), ("/api/updateSSToken", "ss_token") }) {
    var bad = await Read(await client.PostAsJsonAsync(pair.Item1, new Dictionary<string,string>{{pair.Item2,"invalid-token"}}));
    Check(bad.Status == 400 && bad.Json.GetProperty("sdk_error_code").GetInt32() == 1002, "Reject invalid token: " + pair.Item1);
}
var missing = await Read(await client.GetAsync("/api/getCode"));
Check(missing.Status == 400, "Missing UID");
var empty = await Read(await client.PostAsJsonAsync("/api/getSSToken", new {}));
Check(empty.Status == 400, "Missing Code");
var malformed = await Read(await client.PostAsync("/api/getSSToken", new StringContent("{", Encoding.UTF8, "application/json")));
Check(malformed.Status == 400 && malformed.Json.GetProperty("ret_code").GetInt32() == 1, "Malformed JSON");
Console.WriteLine("All HTTP smoke tests passed. Tokens were not printed.");
