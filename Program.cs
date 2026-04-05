using System.Net;
using System.Text;
using System.Web;
using System.Xml;
using MySql.Data.MySqlClient;
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
string html_template = @"<!DOCTYPE html><html lang=""ru""><head><meta charset=""UTF-8""><style> a, abbr { text-decoration: none; } </style>
<title>%title%</title></head><body><center><form action=""%form%"">%body%<button type=""submit"">Start</button></form><br>%result%</center></body></html>";

app.MapGet("/resized-pages", (HttpContext context) =>
{
    string resized_template = html_template.Replace("%title%", "Статистика улучшенных статей за период").Replace("%form%", "resized-pages").Replace("%body%",
        @"<label for=""inwikiproject"">Категория:Статьи проекта </label><input type=""text"" name=""inwikiproject"" value=""%inwikiproject%"" required>
<label for=""startyear"">С года </label><input type=""number"" name=""startyear"" value=""%startyear%"" required>
<label for=""endyear"">По год </label><input type=""number"" name=""endyear"" value=""%endyear%"" required>");
    var parameters = HttpUtility.ParseQueryString(context.Request.QueryString.ToString());
    if (parameters.Count == 0)
        return Results.Content(resized_template.Replace("%result%", "").Replace("%inwikiproject%", "").Replace("%startyear%", (DateTime.Now.Year - 1).ToString())
        .Replace("%endyear%", (DateTime.Now.Year).ToString()), "text/html; charset=utf-8");
    string inwikiproject = parameters[0];
    int startyear = Convert.ToInt32(parameters[1]);
    int endyear = Convert.ToInt32(parameters[2]);
    if (endyear < startyear)
        return Results.Content(resized_template.Replace("%result%", "Конечный год не должен быть больше начального").Replace("%inwikiproject%", inwikiproject)
            .Replace("%startyear%", startyear.ToString()).Replace("%endyear%", endyear.ToString()), "text/html; charset=utf-8");
    var pages = new List<page>();
    string cont = "", query = "https://ru.wikipedia.org/w/api.php?action=query&list=categorymembers&format=xml&cmtitle=К:Статьи проекта " + inwikiproject + "&cmprop=title&cmnamespace=1&cmtype=page&cmlimit=max";
    var creds = Environment.GetEnvironmentVariable("CREDS").Split('\n'); var site = login("ru.wikipedia", creds[0], creds[1], creds[3]);
    while (cont != null) {
        string apiout = cont == "" ? site.GetStringAsync(query).Result : site.GetStringAsync(query + "&cmcontinue=" + Uri.EscapeDataString(cont)).Result;
        using (var r = new XmlTextReader(new StringReader(apiout))) {
            r.WhitespaceHandling = WhitespaceHandling.None;
            r.Read(); r.Read(); r.Read(); cont = r.GetAttribute("cmcontinue");
            while (r.Read())
                if (r.NodeType == XmlNodeType.Element && r.Name == "cm") {
                    var title = r.GetAttribute("title");
                    var p = new page() { title = title.Substring(title.IndexOf(':') + 1) };
                    pages.Add(p);
                }
        }
    }
    foreach (var p in pages) {
        string apiout = site.GetStringAsync("https://ru.wikipedia.org/w/api.php?action=query&prop=revisions&format=xml&rvprop=size&rvlimit=1&rvstart=" + startyear + "-01-01T00:00:00Z&titles=" +
            Uri.EscapeDataString(p.title)).Result;
        using (var r = new XmlTextReader(new StringReader(apiout))) {
            r.WhitespaceHandling = WhitespaceHandling.None;
            while (r.Read())
                if (r.NodeType == XmlNodeType.Element && r.Name == "rev")
                    p.oldsize = Convert.ToInt32(r.GetAttribute("size"));

        }
        if (p.oldsize != 0) {
            apiout = site.GetStringAsync("https://ru.wikipedia.org/w/api.php?action=query&prop=revisions&format=xml&rvprop=size&rvlimit=1&rvstart=" + endyear + "-01-01T00:00:00Z&titles=" +
                Uri.EscapeDataString(p.title)).Result;
            using (var r = new XmlTextReader(new StringReader(apiout))) {
                r.WhitespaceHandling = WhitespaceHandling.None;
                while (r.Read())
                    if (r.NodeType == XmlNodeType.Element && r.Name == "rev")
                        p.newsize = Convert.ToInt32(r.GetAttribute("size"));
            }
        }
        p.times = (float)p.newsize / p.oldsize;
    }

    string result = "<table border=\"1\" cellspacing=\"0\"><tr><th>Статья</th><th>Изменила размер во столько раз</th><th>На столько байт</th></tr>\n";
    foreach (var u in pages.OrderByDescending(u => u.times))
        if (u.oldsize != 0 && u.oldsize != u.newsize)
            result += "<tr><td><a href=\"https://ru.wikipedia.org/wiki/" + Uri.EscapeDataString(u.title) + "\">" + u.title + "</a></td><td>" + u.times + "</td><td>" + (u.newsize - u.oldsize) +
                "</td></tr>\n";
    return Results.Content(resized_template.Replace("%result%", result + "</table>").Replace("%inwikiproject%", inwikiproject)
            .Replace("%startyear%", startyear.ToString()).Replace("%endyear%", endyear.ToString()), "text/html; charset=utf-8");
});

app.MapGet("/transclusions-count", (HttpContext context) =>
{
    string transclusions_template = html_template.Replace("%title%", "Transclusions count in %cat%").Replace("%form%", "transclusions-count").Replace("%body%",
        @"Pages in <input type=""text"" name=""wiki"" value=""%wiki%"" size=""11"" required>
From category <input type=""text"" name=""cat"" value=""%cat%"" placeholder=""without Category: prefix"">
with subcats to depth <input type=""number"" name=""depth"" value=""%depth%"" style=""width:2em"">");
    Dictionary<string, int> pages = new Dictionary<string, int>();
    var parameters = HttpUtility.ParseQueryString(context.Request.QueryString.ToString());
    if (parameters.Count == 0)
        return Results.Content(transclusions_template.Replace("%result%", "").Replace("%wiki%", "ru.wikipedia").Replace("%cat%", "").Replace("%depth%", "0"), "text/html; charset=utf-8");
    string wiki = parameters["wiki"]; string cat = parameters["cat"].Trim() ?? ""; int requireddepth = Convert.ToInt16(parameters["depth"]);
    if (requireddepth < 0)
        return Results.Content(transclusions_template.Replace("%result%", "Use non-negative depth value").Replace("%wiki%", wiki).Replace("%cat%", cat).Replace("%depth%", "0"), "text/html; charset=utf-8");
    if (cat == "")
        return Results.Content(transclusions_template.Replace("%result%", "Enter the category name").Replace("%wiki%", wiki).Replace("%cat%", cat).Replace("%depth%", requireddepth.ToString()), "text/html; charset=utf-8");
    var creds = Environment.GetEnvironmentVariable("CREDS").Split('\n'); var site = login("ru.wikipedia", creds[0], creds[1], creds[3]);
    using (var r = new XmlTextReader(new StringReader(site.GetStringAsync("https://" + wiki + ".org/w/api.php?action=query&prop=pageprops&format=xml&titles=category:" + Uri.EscapeDataString(cat)).Result)))
        while (r.Read())
            if (r.Name == "page" && r.GetAttribute("_idx") == "-1")
                return Results.Content(transclusions_template.Replace("%result%", "There is no Category:" + cat + " in " + wiki).Replace("%wiki%", wiki).Replace("%cat%", cat).Replace("%depth%", requireddepth.ToString()), "text/html; charset=utf-8");
    if (cat != "")
        searchsubcats(cat, 0, requireddepth, site, wiki, pages);
    if (pages.Count == 0)
        return Results.Content(transclusions_template.Replace("%result%", "There are no pages in this category").Replace("%wiki%", wiki).Replace("%cat%", cat).Replace("%depth%", requireddepth.ToString()), "text/html; charset=utf-8");
    else {
        foreach (var page in pages.Keys) {
            string cont = "", query = "https://ru.wikipedia.org/w/api.php?action=query&format=xml&list=embeddedin&eititle=" + Uri.EscapeDataString(page) + "&eilimit=max";
            int counter = 0;
            while (cont != null) {
                var apiout = cont == "" ? site.GetStringAsync(query).Result : site.GetStringAsync(query + "&eicontinue=" + Uri.EscapeDataString(cont)).Result;
                using (var r = new XmlTextReader(new StringReader(apiout))) {
                    r.Read(); r.Read(); r.Read(); cont = r.GetAttribute("eicontinue");
                    while (r.Read())
                        if (r.Name == "ei")
                            counter++;
                }
            }
            pages[page] = counter;
        }
        string result = "<table border=\"1\" cellspacing=\"0\"><tr><th>Page</th><th>Transclusions</th></tr>\n";
        foreach (var p in pages.OrderByDescending(p => p.Value))
            result += "<tr><td><a target=\"_blank\" href=\"https://" + wiki + ".org/wiki/" + Uri.EscapeDataString(p.Key).Replace("%3A", ":").Replace("%20", "_") + "\">" + p.Key + "</a></td><td>" + p.Value + "</td></tr>\n";
        return Results.Content(transclusions_template.Replace("%result%", result + "</table>").Replace(" % wiki%", wiki).Replace("%cat%", cat).Replace("%depth%", requireddepth.ToString()), "text/html; charset=utf-8");
    }
});

app.MapGet("/likes", (HttpContext context) =>
{
    string likes_template = html_template.Replace("%title%", "Likes from and to user %user%").Replace("%form%", "likes").Replace("%body%",
        @"User: <input name=""user"" type=""text"" value=""%user%"" required>Wiki: <input type=""text"" name=""wiki"" value=""%wiki%"" required>");
    var parameters = HttpUtility.ParseQueryString(context.Request.QueryString.ToString());
    if (parameters.Count == 0)
        return Results.Content(likes_template.Replace("%result%", "").Replace("%user%", "").Replace("%wiki%", "ru.wikipedia"), "text/html; charset=utf-8");
    var thanked = new Dictionary<string, int>(); var thankers = new Dictionary<string, int>(); var users = new HashSet<string>(); MySqlDataReader r; MySqlCommand command;
    string user = parameters["user"]; string wiki = parameters["wiki"]; var creds = Environment.GetEnvironmentVariable("CREDS").Split('\n');
    var connect = new MySqlConnection(creds[2].Replace("%project%", url2db(wiki))); connect.Open();
    command = new MySqlCommand("select cast(replace (log_title, '_', ' ') as char) from logging where log_type=\"thanks\" and log_actor=(select actor_id from actor where actor_name=\"" + user + "\");", connect) { CommandTimeout = 9999 };
    r = command.ExecuteReader();
    while (r.Read()) {
        string name = r.GetString(0);
        if (!thanked.ContainsKey(name))
            thanked.Add(name, 1);
        else
            thanked[name]++;
    }
    r.Close();
    command = new MySqlCommand("select cast(actor_name as char) source from (select log_actor from logging where log_type=\"thanks\" and log_title=\"" + user.Replace(' ', '_') + "\") log join actor on actor_id=log_actor;", connect) { CommandTimeout = 9999 };
    r = command.ExecuteReader();
    while (r.Read()) {
        string name = r.GetString(0);
        if (!thankers.ContainsKey(name))
            thankers.Add(name, 1);
        else
            thankers[name]++;
    }
    string response = "<br><br>\n<table><tr><td valign=\"top\"><table border=\"1\" cellspacing=\"0\">";
    foreach (var t in thanked.OrderByDescending(t => t.Value))
        response += "<tr><td>" + user + " <a href=\"https://" + wiki + ".org/w/index.php?title=special:log&type=thanks&user=" + Uri.EscapeDataString(user) + "&page=" + t.Key + "\">🡲</a> " +
        "<a href=\"https://mbh.toolforge.org/likes?user=" + Uri.EscapeDataString(t.Key) + "&wiki=" + wiki + "\">" + t.Key + "</a></td><td>" + t.Value + "</td></tr>\n";
    response += "</table></td><td valign=\"top\"><table border=\"1\" cellspacing=\"0\">";
    foreach (var t in thankers.OrderByDescending(t => t.Value))
        response += "<tr><td><a href=\"https://mbh.toolforge.org/likes?user=" + Uri.EscapeDataString(t.Key) + "&wiki=" + wiki + "\">" + t.Key + "</a> <a href=\"https://" + wiki +
        ".org/w/index.php?title=special:log&type=thanks&user=" + t.Key + "&page=" + Uri.EscapeDataString(user) + "\">🡲</a>" + user + " </td><td>" + t.Value + "</td></tr>\n";
    return Results.Content(likes_template.Replace("%result%", response + "</table></td></tr></table>").Replace("%user%", user).Replace("%wiki%", wiki), "text/html; charset=utf-8");
});

app.MapGet("/patstats", (HttpContext context) =>
{
    Dictionary<string, stat> usertable = new Dictionary<string, stat>();
    var parameters = HttpUtility.ParseQueryString(context.Request.QueryString.ToString());
    if (parameters.Count == 0)
        return Results.Content(patstats_response("db", "ru.wikipedia", DateTime.Now.ToString("yyyy-MM-dd"), DateTime.Now.ToString("yyyy-MM-dd"), "all", "", html_template), "text/html; charset=utf-8");
    string type = parameters["type"];
    string project = parameters["project"];
    string startdate = parameters["startdate"];
    string enddate = parameters["enddate"];
    string sort = parameters["sort"];
    string answer = "";
    if (type == "db") {
        var creds = Environment.GetEnvironmentVariable("CREDS").Split('\n'); var connect = new MySqlConnection(creds[2].Replace("%project%", url2db(project))); connect.Open();
        var squery = new MySqlCommand("select log_action, log_namespace, cast(actor_name as char) user from logging join actor on log_actor=actor_id where log_type=\"review\" and " +
            "log_timestamp >" + startdate.Replace("-", "") + "000000 and log_timestamp<" + enddate.Replace("-", "") + "235959", connect);
        var r = squery.ExecuteReader();
        while (r.Read()) {
            string user = r.GetString("user");
            if (user == null)
                continue;
            var buffer = new byte[10];
            r.GetBytes(0, 0, buffer, 0, 10);
            int ns = r.GetInt16("log_namespace");
            put_new_action(user, Encoding.UTF8.GetString(buffer, 0, buffer.Length), ns, usertable);
        }
    }
    if (type == "api") {
        string cont = "", query = "https://" + project + ".org/w/api.php?action=query&format=xml&list=logevents&leprop=title|user|type&letype=review&leend=" + startdate +
                "T00:00:00Z&lestart=" + enddate + "T23:59:59Z&lelimit=max";
        var creds = Environment.GetEnvironmentVariable("CREDS").Split('\n'); var site = login(project, creds[0], creds[1], creds[3]);
        while (cont != null) {
            using (var xr = new XmlTextReader(new StringReader(cont == "" ? site.GetStringAsync(query).Result : site.GetStringAsync(query + "&lecontinue=" + cont).Result))) {
                xr.Read(); xr.Read(); xr.Read(); cont = xr.GetAttribute("lecontinue");
                while (xr.Read())
                    if (xr.Name == "item") {
                        string user = xr.GetAttribute("user");
                        if (user == null)
                            continue;
                        put_new_action(user, xr.GetAttribute("action"), Convert.ToInt16(xr.GetAttribute("ns")), usertable);
                    }
            }
        }
    }
    int c = 0;
    answer = "<table border=\"1\" cellspacing=\"0\"><tr><th>№</th><th>User</th><th>All</th><th>Articles</th><th>Templates</th><th>Categories</th><th>Files</th><th>Portals</th><th>Modules</th><th>Unreviews</th></tr>\n";
    foreach (var u in usertable.OrderByDescending(u => sort == "main" ? u.Value.main : (sort == "template" ? u.Value.template : (sort == "cat" ? u.Value.cat : (sort == "file" ? u.Value.file :
    (sort == "portal" ? u.Value.portal : (sort == "module" ? u.Value.module : (sort == "unpat" ? u.Value.unpat : u.Value.sum))))))))
        answer += "<tr><td>" + ++c + "</td><td><a href=\"https://" + project + ".org/wiki/special:log?type=review&user=" + Uri.EscapeDataString(u.Key) + "\">" + u.Key + "</a></td><td>" +
            u.Value.sum + "</td><td>" + u.Value.main + "</td><td>" + u.Value.template + "</td><td>" + u.Value.cat + "</td><td>" + u.Value.file + "</td><td>" + u.Value.portal + "</td><td>" +
            u.Value.module + "</td><td>" + u.Value.unpat + "</td></tr>";
    return Results.Content(patstats_response(type, project, startdate, enddate, sort, answer + "</table>", html_template), "text/html; charset=utf-8");
});

app.Run();

HttpClient login(string project, string login, string password, string ua) {
    var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, UseCookies = true, CookieContainer = new CookieContainer() }); client.DefaultRequestHeaders.Add("User-Agent", ua);
    var result = client.GetAsync("https://" + project + ".org/w/api.php?action=query&meta=tokens&type=login&format=xml").Result; var doc = new XmlDocument(); doc.LoadXml(result.Content
        .ReadAsStringAsync().Result); var logintoken = doc.SelectSingleNode("//tokens/@logintoken").Value; result = client.PostAsync("https://" + project + ".org/w/api.php", new
            FormUrlEncodedContent(new Dictionary<string, string> { { "action", "login" }, { "lgname", login }, { "lgpassword", password }, { "lgtoken", logintoken }, { "format", "xml" } })).Result; return client;
}
string url2db(string url) { return url.Replace(".", "").Replace("wikipedia", "wiki"); }
static void searchsubcats(string category, int currentdepth, int requireddepth, HttpClient site, string wiki, Dictionary<string, int> pages) {
    string cont = "", query = "https://" + wiki + ".org/w/api.php?action=query&list=categorymembers&format=xml&cmtitle=Category:" + Uri.EscapeDataString(category) + "&cmprop=title&cmlimit=max&cmnamespace=10|828";
    while (cont != null)
    {
        var apiout = cont == "" ? site.GetStringAsync(query).Result : site.GetStringAsync(query + "&cmcontinue=" + Uri.EscapeDataString(cont)).Result;
        using (var r = new XmlTextReader(new StringReader(apiout)))
        {
            r.Read(); r.Read(); r.Read(); cont = r.GetAttribute("cmcontinue");
            while (r.Read())
                if (r.Name == "cm")
                    if (!pages.ContainsKey(r.GetAttribute("title")))
                        pages.Add(r.GetAttribute("title"), 0);
        }
    }
    cont = "";
    query = "https://" + wiki + ".org/w/api.php?action=query&list=categorymembers&format=xml&cmtitle=Category:" + Uri.EscapeDataString(category) + "&cmnamespace=14&cmprop=title&cmlimit=max";
    while (cont != null)
    {
        var apiout = cont == "" ? site.GetStringAsync(query).Result : site.GetStringAsync(query + "&cmcontinue=" + Uri.EscapeDataString(cont)).Result;
        using (var r = new XmlTextReader(new StringReader(apiout)))
        {
            r.WhitespaceHandling = WhitespaceHandling.None;
            r.Read(); r.Read(); r.Read(); cont = r.GetAttribute("cmcontinue");
            while (r.Read())
                if (r.Name == "cm")
                {
                    string fullcategoryname = r.GetAttribute("title");
                    string shortcategoryname = fullcategoryname.Substring(fullcategoryname.IndexOf(':') + 1);
                    if (currentdepth + 1 <= requireddepth)
                        searchsubcats(shortcategoryname, currentdepth + 1, requireddepth, site, wiki, pages);
                }
        }
    }
}
static void put_new_action(string user, string type, int ns, Dictionary<string, stat> usertable)
{
    if (usertable.ContainsKey(user)) {
        usertable[user].sum++;
        if (type.Contains("un"))
            usertable[user].unpat++;
        if (ns == 0)
            usertable[user].main++;
        else if (ns == 10)
            usertable[user].template++;
        else if (ns == 14)
            usertable[user].cat++;
        else if (ns == 6)
            usertable[user].file++;
        else if (ns == 100)
            usertable[user].portal++;
        else if (ns == 828)
            usertable[user].module++;
    }
    else {
        int main, template, file, cat, portal, module, unpat, sum;
        unpat = type.Contains("un") ? 1 : 0;
        main = ns == 0 ? 1 : 0;
        file = ns == 6 ? 1 : 0;
        template = ns == 10 ? 1 : 0;
        cat = ns == 14 ? 1 : 0;
        portal = ns == 100 ? 1 : 0;
        module = ns == 828 ? 1 : 0;
        sum = 1;
        var stats = new stat { unpat = unpat, main = main, file = file, template = template, cat = cat, portal = portal, module = module, sum = sum };
        usertable.Add(user, stats);
    }
}
static string patstats_response(string type, string project, string startdate, string enddate, string sort, string answer, string html_template)
{
    string result = html_template.Replace("%title%", "FlaggedRevs user activity").Replace("%form%", "patstats").Replace("%body%",
        @"Retrieve data from <label><input type=""radio"" name=""type"" value=""db"" %checked_db%>database (faster for very large time periods)</label>
<label><input type=""radio"" name=""type"" value=""api"" %checked_api%>API</label><br><br>
<label>Wiki: <input type=""text"" name=""project"" value=""%project%"" required></label>
<label>From <input type=""date"" name=""startdate"" value=""%startdate%"" required></label>
<label>To <input type=""date"" name=""enddate"" value=""%enddate%"" required> (including the date)</label>
<br><br>Order by num of actions in 
<label><input type=""radio"" name=""sort"" value=""all"" %checked_all%>all</label>
<label><input type=""radio"" name=""sort"" value=""main"" %checked_main%>articles</label>
<label><input type=""radio"" name=""sort"" value=""template"" %checked_template%>templates</label>
<label><input type=""radio"" name=""sort"" value=""cat"" %checked_cat%>categories</label>
<label><input type=""radio"" name=""sort"" value=""file"" %checked_file%>files</label>
<label><input type=""radio"" name=""sort"" value=""portal"" %checked_portal%>portals</label>
<label><input type=""radio"" name=""sort"" value=""module"" %checked_module%>modules</label>
<label><input type=""radio"" name=""sort"" value=""unpat"" %checked_unpat%>unreviews</label>").Replace("%result%", answer).Replace("%project%", project).Replace("%startdate%", startdate).Replace("%enddate%", enddate);
    if (type == "db")
        result = result.Replace("%checked_db%", "checked");
    else
        result = result.Replace("%checked_api%", "checked");
    if (sort == "all")
        result = result.Replace("%checked_all%", "checked");
    else if (sort == "main")
        result = result.Replace("%checked_main%", "checked");
    else if (sort == "template")
        result = result.Replace("%checked_template%", "checked");
    else if (sort == "file")
        result = result.Replace("%checked_file%", "checked");
    else if (sort == "cat")
        result = result.Replace("%checked_cat%", "checked");
    else if (sort == "portal")
        result = result.Replace("%checked_portal%", "checked");
    else if (sort == "module")
        result = result.Replace("%checked_module%", "checked");
    else
        result = result.Replace("%checked_unpat%", "checked");
    return result;
}
class page { public required string title; public int oldsize, newsize; public float times; }
class stat { public int main, template, cat, file, portal, unpat, module, sum; }
