using System.Web;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/service", (HttpContext context) =>
{
    string rawQuery = context.Request.QueryString.ToString();
    var queryParams = HttpUtility.ParseQueryString(rawQuery);
    var items = queryParams.AllKeys.Select(key => $"<li><b>{key}:</b> {queryParams[key]}</li>");

    string htmlResult = $@"
        <html>
            <body style='font-family: Arial; padding: 20px;'>
                <h2>Результат парсинга через HttpUtility:</h2>
                <ul>{string.Join("", items)}</ul>
                <hr>
                <small>Сырая строка: {rawQuery}</small>
            </body>
        </html>";

    return Results.Content(htmlResult, "text/html; charset=utf-8");
});
app.Run();