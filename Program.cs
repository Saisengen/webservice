using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using System.Net;
using System.Text;
using System.Web;
using System.Xml;
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
string html_template = @"<!DOCTYPE html><html lang=""ru""><head><meta charset=""UTF-8""><style> a, abbr { text-decoration: none; } </style><title>%title%</title></head><body><center>
<form action=""%form%"">%body% <button type=""submit"">Start</button></form><br>%result%</center></body></html>", meta = "text/html; charset=utf-8";
var creds = new StreamReader(Environment.GetEnvironmentVariable("TOOL_DATA_DIR") + "/p").ReadToEnd().Split('\n');

app.MapGet("/resized-pages", (HttpContext context) =>
{
    string resized_template = html_template.Replace("%title%", "Статистика улучшенных статей за период").Replace("%form%", "resized-pages").Replace("%body%",
        @"<label for=""inwikiproject"">Категория:Статьи проекта </label><input type=""text"" name=""inwikiproject"" value=""%inwikiproject%"" required>
<label for=""startyear"">С года </label><input type=""number"" name=""startyear"" value=""%startyear%"" required>
<label for=""endyear"">По год </label><input type=""number"" name=""endyear"" value=""%endyear%"" required>");
    var parameters = HttpUtility.ParseQueryString(context.Request.QueryString.ToString());
    if (parameters.Count == 0)
        return Results.Content(resized_template.Replace("%result%", "").Replace("%inwikiproject%", "").Replace("%startyear%", (DateTime.Now.Year - 1).ToString())
        .Replace("%endyear%", (DateTime.Now.Year).ToString()), meta);
    string inwikiproject = parameters[0]; int startyear = i(parameters[1]); int endyear = i(parameters[2]);
    if (endyear < startyear)
        return Results.Content(resized_template.Replace("%result%", "Конечный год не должен быть больше начального").Replace("%inwikiproject%", inwikiproject)
            .Replace("%startyear%", startyear.ToString()).Replace("%endyear%", endyear.ToString()), meta);
    var pages = new List<page>();
    string cont = "", query = "https://ru.wikipedia.org/w/api.php?action=query&list=categorymembers&format=xml&cmtitle=К:Статьи проекта " + inwikiproject + "&cmprop=title&cmnamespace=1&cmtype=page&cmlimit=max";
    var site = login("ru.wikipedia", creds[0], creds[1], creds[3]);
    while (cont != null) {
        var r = new XmlTextReader(new StringReader(cont == "" ? site.GetStringAsync(query).Result : site.GetStringAsync(query + "&cmcontinue=" + Uri.EscapeDataString(cont)).Result));
        r.Read(); r.Read(); r.Read(); cont = r.GetAttribute("cmcontinue"); while (r.Read())
            if (r.NodeType == XmlNodeType.Element && r.Name == "cm") {
                var title = r.GetAttribute("title"); var p = new page() { title = title.Substring(title.IndexOf(':') + 1) }; pages.Add(p);
            }
    }
    foreach (var p in pages) {
        string apiout = site.GetStringAsync("https://ru.wikipedia.org/w/api.php?action=query&prop=revisions&format=xml&rvprop=size&rvlimit=1&rvstart=" + startyear + "-01-01T00:00:00Z&titles=" +
            Uri.EscapeDataString(p.title)).Result;
        var r = new XmlTextReader(new StringReader(apiout)); while (r.Read())
            if (r.NodeType == XmlNodeType.Element && r.Name == "rev")
                p.oldsize = i(r.GetAttribute("size"));
        if (p.oldsize != 0) {
            apiout = site.GetStringAsync("https://ru.wikipedia.org/w/api.php?action=query&prop=revisions&format=xml&rvprop=size&rvlimit=1&rvstart=" + endyear + "-01-01T00:00:00Z&titles=" +
                Uri.EscapeDataString(p.title)).Result;
            r = new XmlTextReader(new StringReader(apiout)); while (r.Read())
                if (r.NodeType == XmlNodeType.Element && r.Name == "rev")
                    p.newsize = i(r.GetAttribute("size"));
        }
        p.times = (float)p.newsize / p.oldsize;
    }

    string result = "<table border=\"1\" cellspacing=\"0\"><tr><th>Статья</th><th>Изменила размер во столько раз</th><th>На столько байт</th></tr>\n";
    foreach (var u in pages.OrderByDescending(u => u.times))
        if (u.oldsize != 0 && u.oldsize != u.newsize)
            result += "<tr><td><a href=\"https://ru.wikipedia.org/wiki/" + Uri.EscapeDataString(u.title) + "\">" + u.title + "</a></td><td>" + u.times + "</td><td>" + (u.newsize - u.oldsize) +
                "</td></tr>\n";
    return Results.Content(resized_template.Replace("%result%", result + "</table>").Replace("%inwikiproject%", inwikiproject)
            .Replace("%startyear%", startyear.ToString()).Replace("%endyear%", endyear.ToString()), meta);
});

app.MapGet("/transclusions-count", (HttpContext context) =>
{
    string transclusions_template = html_template.Replace("%title%", "Transclusions count in %cat%").Replace("%form%", "transclusions-count").Replace("%body%",
        @"Pages in <input type=""text"" name=""wiki"" value=""%wiki%"" size=""11"" required>
From category <input type=""text"" name=""cat"" value=""%cat%"" placeholder=""without Category: prefix"">
with subcats to depth <input type=""number"" name=""depth"" value=""%depth%"" style=""width:2em"">");
    var pages = new page_authors_stats() { list = new Dictionary<string, int>()}; var parameters = HttpUtility.ParseQueryString(context.Request.QueryString.ToString());
    if (parameters.Count == 0)
        return Results.Content(transclusions_template.Replace("%result%", "").Replace("%wiki%", "ru.wikipedia").Replace("%cat%", "").Replace("%depth%", "0"), meta);
    string wiki = parameters["wiki"]; string cat = parameters["cat"].Trim() ?? ""; int requireddepth = i(parameters["depth"]);
    if (requireddepth < 0)
        return Results.Content(transclusions_template.Replace("%result%", "Use non-negative depth value").Replace("%wiki%", wiki).Replace("%cat%", cat).Replace("%depth%", "0"), meta);
    if (cat == "")
        return Results.Content(transclusions_template.Replace("%result%", "Enter the category name").Replace("%wiki%", wiki).Replace("%cat%", cat).Replace("%depth%", requireddepth.ToString()), meta);
    var site = login("ru.wikipedia", creds[0], creds[1], creds[3]);
    using (var r = new XmlTextReader(new StringReader(site.GetStringAsync("https://" + wiki + ".org/w/api.php?action=query&prop=pageprops&format=xml&titles=category:" + Uri.EscapeDataString(cat)).Result)))
        while (r.Read())
            if (r.Name == "page" && r.GetAttribute("_idx") == "-1")
                return Results.Content(transclusions_template.Replace("%result%", "There is no Category:" + cat + " in " + wiki).Replace("%wiki%", wiki).Replace("%cat%", cat).Replace("%depth%", requireddepth.ToString()), meta);
    searchsubcats(cat, 0, requireddepth, site, wiki, pages);
    if (pages.list.Count == 0)
        return Results.Content(transclusions_template.Replace("%result%", "There are no pages in this category").Replace("%wiki%", wiki).Replace("%cat%", cat).Replace("%depth%", requireddepth.ToString()), meta);
    else {
        foreach (var page in pages.list.Keys) {
            string cont = "", query = "https://ru.wikipedia.org/w/api.php?action=query&format=xml&list=embeddedin&eititle=" + Uri.EscapeDataString(page) + "&eilimit=max";
            int counter = 0;
            while (cont != null) {
                var r = new XmlTextReader(new StringReader(cont == "" ? site.GetStringAsync(query).Result : site.GetStringAsync(query + "&eicontinue=" + Uri.EscapeDataString(cont)).Result));
                r.Read(); r.Read(); r.Read(); cont = r.GetAttribute("eicontinue");
                while (r.Read())
                    if (r.Name == "ei")
                        counter++;
            }
            pages.list[page] = counter;
        }
        string result = "<table border=\"1\" cellspacing=\"0\"><tr><th>Page</th><th>Transclusions</th></tr>\n";
        foreach (var p in pages.list.OrderByDescending(p => p.Value))
            result += "<tr><td><a target=\"_blank\" href=\"https://" + wiki + ".org/wiki/" + Uri.EscapeDataString(p.Key).Replace("%3A", ":").Replace("%20", "_") + "\">" + p.Key + "</a></td><td>" + p.Value + "</td></tr>\n";
        return Results.Content(transclusions_template.Replace("%result%", result + "</table>").Replace(" % wiki%", wiki).Replace("%cat%", cat).Replace("%depth%", requireddepth.ToString()), meta);
    }
});

app.MapGet("/likes", (HttpContext context) =>
{
    string likes_template = html_template.Replace("%title%", "Likes from and to user %user%").Replace("%form%", "likes").Replace("%body%",
        @"User: <input name=""user"" type=""text"" value=""%user%"" required>Wiki: <input type=""text"" name=""wiki"" value=""%wiki%"" required>");
    var parameters = HttpUtility.ParseQueryString(context.Request.QueryString.ToString());
    if (parameters.Count == 0)
        return Results.Content(likes_template.Replace("%result%", "").Replace("%user%", "").Replace("%wiki%", "ru.wikipedia"), meta);
    var thanked = new Dictionary<string, int>(); var thankers = new Dictionary<string, int>(); var users = new HashSet<string>(); MySqlDataReader r; MySqlCommand command;
    string user = parameters["user"]; string wiki = parameters["wiki"]; var connect = new MySqlConnection(creds[2].Replace("%project%", url2db(wiki))); connect.Open();
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
    return Results.Content(likes_template.Replace("%result%", response + "</table></td></tr></table>").Replace("%user%", user).Replace("%wiki%", wiki), meta);
});

app.MapGet("/patstats", (HttpContext context) =>
{
    Dictionary<string, stat> usertable = new Dictionary<string, stat>();
    var prms = HttpUtility.ParseQueryString(context.Request.QueryString.ToString());
    if (prms.Count == 0)
        return Results.Content(patstats_response("db", "ru.wikipedia", DateTime.Now.ToString("yyyy-MM-dd"), DateTime.Now.ToString("yyyy-MM-dd"), "all", "", html_template), meta);
    string type = prms["type"]; string project = prms["project"]; string sort = prms["sort"]; string startdate, enddate; var date1 = DateTime.Parse(prms["startdate"]); var date2 = DateTime.Parse(prms["enddate"]);
    if (date1 <= date2) { startdate = prms["startdate"]; enddate = prms["enddate"]; } else { startdate = prms["enddate"]; enddate = prms["startdate"]; } //чтобы работало корректно, если первая дата позже второй
    if (type == "db") {
        var connect = new MySqlConnection(creds[2].Replace("%project%", url2db(project))); connect.Open();
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
        var site = login(project, creds[0], creds[1], creds[3]);
        string cont = "", query = "https://" + project + ".org/w/api.php?action=query&format=xml&list=logevents&leprop=title|user|type&letype=review&leend=" + startdate +
                "T00:00:00Z&lestart=" + enddate + "T23:59:59Z&lelimit=max";
        while (cont != null) {
            var xr = new XmlTextReader(new StringReader(cont == "" ? site.GetStringAsync(query).Result : site.GetStringAsync(query + "&lecontinue=" + cont).Result));
            xr.Read(); xr.Read(); xr.Read(); cont = xr.GetAttribute("lecontinue"); while (xr.Read())
                if (xr.Name == "item") {
                    string user = xr.GetAttribute("user");
                    if (user == null)
                        continue;
                    put_new_action(user, xr.GetAttribute("action"), i(xr.GetAttribute("ns")), usertable);
                }
        }
    }
    int c = 0; string answer = "<table border=\"1\" cellspacing=\"0\"><tr><th>№</th><th>User</th><th>All</th><th>Articles</th><th>Templates</th><th>Categories</th><th>Files</th><th>Portals</th>" +
    "<th>Modules</th><th>Unreviews</th></tr>\n";
    foreach (var u in usertable.OrderByDescending(u => sort == "main" ? u.Value.main : (sort == "template" ? u.Value.template : (sort == "cat" ? u.Value.cat : (sort == "file" ? u.Value.file :
    (sort == "portal" ? u.Value.portal : (sort == "module" ? u.Value.module : (sort == "unpat" ? u.Value.unpat : u.Value.sum))))))))
        answer += "<tr><td>" + ++c + "</td><td><a href=\"https://" + project + ".org/wiki/special:log?type=review&user=" + Uri.EscapeDataString(u.Key) + "\">" + u.Key + "</a></td><td>" +
            u.Value.sum + "</td><td>" + u.Value.main + "</td><td>" + u.Value.template + "</td><td>" + u.Value.cat + "</td><td>" + u.Value.file + "</td><td>" + u.Value.portal + "</td><td>" +
            u.Value.module + "</td><td>" + u.Value.unpat + "</td></tr>";
    return Results.Content(patstats_response(type, project, startdate, enddate, sort, answer + "</table>", html_template), meta);
});

app.MapGet("/unreviewed-pages", (HttpContext context) =>
{
    HashSet<string> candidates = new HashSet<string>(); Dictionary<string, pageinfo_oldreviewed> pages = new Dictionary<string, pageinfo_oldreviewed>();
    var prms = HttpUtility.ParseQueryString(context.Request.QueryString.ToString());
    if (prms.Count == 0)
        return Results.Content(unreviewed_response("ru.wikipedia", "", "", 0, "", false, html_template), meta);
    bool talks = prms["talks"] == "on"; string wiki = prms["wiki"]; string cat = prms["cat"].Trim() ?? ""; string template = prms["template"].Trim() ?? ""; int requireddepth = i(prms["depth"]);
    if (requireddepth < 0)
        return Results.Content(unreviewed_response(wiki, cat, template, 0, "Use non-negative depth value", talks, html_template), meta);
    if (cat == "" && template == "")
        return Results.Content(unreviewed_response(wiki, cat, template, requireddepth, "Enter the category, template name or both", talks, html_template), meta);
    var site = login(wiki, creds[0], creds[1], creds[3]);
    bool broken_title = false;
    if (cat != "") {
        var r = new XmlTextReader(new StringReader(site.GetStringAsync("https://" + wiki + ".org/w/api.php?action=query&prop=pageprops&format=xml&titles=category:" + Uri.EscapeDataString(cat)).Result));
        while (r.Read())
            if (r.Name == "page" && r.GetAttribute("_idx") == "-1")
                broken_title = true;
    }
    if (template != "") {
        var r = new XmlTextReader(new StringReader(site.GetStringAsync("https://" + wiki + ".org/w/api.php?action=query&prop=pageprops&format=xml&titles=" + Uri.EscapeDataString(template)).Result));
        while (r.Read())
            if (r.Name == "page" && r.GetAttribute("_idx") == "-1")
                broken_title = true;
    }
    if (broken_title)
        return Results.Content(unreviewed_response(wiki, cat, template, requireddepth, "There is no such category or such template in this wiki", talks, html_template), meta);

    if (cat != "")
        searchsubcats_unreviewed(cat, 0, requireddepth, site, wiki, candidates, talks);
    if (template != "") {
        string cont = "", query = "https://" + wiki + ".org/w/api.php?action=query&format=xml&list=embeddedin&eititle=" + Uri.EscapeDataString(template) + "&eilimit=max&einamespace=100|102|0|6|10|14";
        while (cont != null) {
            var r = new XmlTextReader(new StringReader(cont == "" ? site.GetStringAsync(query).Result : site.GetStringAsync(query + "&eicontinue=" + Uri.EscapeDataString(cont)).Result));
            r.Read(); r.Read(); r.Read(); cont = r.GetAttribute("eicontinue"); while (r.Read())
                if (r.Name == "ei" && !candidates.Contains(r.GetAttribute("pageid")))
                    candidates.Add(r.GetAttribute("pageid"));            
        }
    }
    if (candidates.Count == 0)
        return Results.Content(unreviewed_response(wiki, cat, template, requireddepth, "There are no pages in this category or using this template", talks, html_template), meta);

    var requeststrings = new HashSet<string>(); int c = 0; string idset = "";
    foreach (var id in candidates) {
        idset += "|" + id;
        if (++c % (talks ? 10 : 49) == 0) {
            requeststrings.Add(idset.Substring(1));
            idset = "";
        }
    }
    if (idset.Length > 0)
        requeststrings.Add(idset.Substring(1));

    foreach (var rstring in requeststrings) {
        var r = new XmlTextReader(new StringReader(site.GetStringAsync("https://" + wiki + ".org/w/api.php?action=query&format=xml&prop=flagged&" + (talks ? "titles=" : "pageids=") + rstring).Result));
        while (r.Read())
            if (r.Name == "page" && r.NodeType == XmlNodeType.Element) {
                string title = r.GetAttribute("title");
                r.Read();
                if (r.Name != "flagged")
                    pages.Add(title, new pageinfo_oldreviewed() { pending_since = "never", stable_revid = "" });
                else if (r.GetAttribute("pending_since") != null)
                    pages.Add(title, new pageinfo_oldreviewed() { pending_since = r.GetAttribute("pending_since").Substring(0, 10), stable_revid = r.GetAttribute("stable_revid") });
            }                
        }
    if (pages.Count == 0)
        return Results.Content(unreviewed_response(wiki, cat, template, requireddepth, "Last revision of all pages in this category or using this template is reviewed", talks, html_template), meta);
    string result = "<table border=\"1\" cellspacing=\"0\"><tr><th>Page</th><th>Date of first unreviewed revision</th></tr>\n";
    foreach (var p in pages.OrderByDescending(p => p.Value.pending_since)) {
        string link;
        if (p.Value.pending_since == "never")
            link = "https://" + wiki + ".org/wiki/" + Uri.EscapeDataString(p.Key);
        else
            link = "https://" + wiki + ".org/w/index.php?title=" + Uri.EscapeDataString(p.Key) + "&type=revision&diff=cur&oldid=" + p.Value.stable_revid;
        result += "<tr><td><a target=\"_blank\" href=\"" + link + "\">" + p.Key + "</a></td><td>" + p.Value.pending_since + "</td></tr>\n";
    }
    return Results.Content(unreviewed_response(wiki, cat, template, requireddepth, result += "</table></center>", talks, html_template), meta);
});

app.MapGet("/test", (HttpContext context) =>
{
    string result = "<body><canter><table border=1><tr><th>envvar</th><th>value</th><tr>";
    foreach (System.Collections.DictionaryEntry v in Environment.GetEnvironmentVariables())
        result += "<tr><td>" + v.Key + "</td><td>" + (v.Key.ToString().Contains("PASS") ? "" : v.Value) + "</td></tr>";
    return Results.Content(result + "</table></center></body>", meta);
});

app.MapGet("/cpf", (HttpContext context) =>
{
    var prms = HttpUtility.ParseQueryString(context.Request.QueryString.ToString()); var path = new catpath(); var processedcats = new HashSet<string>();
    var cpf_template = new StreamReader(Environment.GetEnvironmentVariable("TOOL_DATA_DIR") + "/cpf.html").ReadToEnd();
    if (prms.Count == 0)
        return Results.Content(cpf_template.Replace("%page%", "").Replace("%uppercat%", "").Replace("%project%", "ru.wikipedia").Replace("%response%", ""), meta);
    string project = prms["project"]; string cat = prms["uppercat"]; string page = prms["page"]; var site = login("ru.wikipedia", creds[0], creds[1], creds[3]);
    string apiout; try {
        apiout = site.GetStringAsync("https://" + project + ".org/w/api.php?action=query&prop=pageprops&format=xml&titles=category:" + Uri.EscapeDataString(cat)).Result;
    }
    catch { return Results.Content(cpf_template.Replace("%page%", page).Replace("%uppercat%", cat).Replace("%project%", "ru.wikipedia").Replace("%response%", "<li>There is no such wiki (" + project + ")</li>"), meta); }
    using (var r = new XmlTextReader(new StringReader(apiout)))
        while (r.Read())
            if (r.Name == "page" && r.GetAttribute("_idx") == "-1") {
                return Results.Content(cpf_template.Replace("%page%", page).Replace("%uppercat%", cat).Replace("%project%", project).Replace("%response%", "<li>There is no such category (Category:" + cat + ") on this wiki</li>"), meta);
            }
    using (var r = new XmlTextReader(new StringReader(site.GetStringAsync("https://" + project + ".org/w/api.php?action=query&prop=pageprops&format=xml&titles=" + Uri.EscapeDataString(page)).Result)))
        while (r.Read())
            if (r.Name == "page" && r.GetAttribute("_idx") == "-1") {
                return Results.Content(cpf_template.Replace("%page%", page).Replace("%uppercat%", cat).Replace("%project%", project).Replace("%response%", "<li>There is no such page (" + cat + ") on this wiki</li>"), meta);
            }
    apiout = site.GetStringAsync("https://" + project + ".org/w/api.php?action=query&format=xml&meta=siteinfo&siprop=namespaces").Result; string localcatname = "";
    using (var r = new XmlTextReader(new StringReader(apiout)))
        while (r.Read())
            if (r.Name == "ns" && r.GetAttribute("id") == "14") { r.Read(); localcatname = r.Value; }

    string purpose_cat = localcatname + ":" + cat;
    search_upcats(project, purpose_cat, page, path, processedcats, site);
    if (path.found) {
        string result = "";
        foreach (var level in path.path)
            result += "<li><a href=\"https://" + project + ".org/wiki/" + level + "\" target=\"_blank\">" + level + "</a></li>\n";
        return Results.Content(cpf_template.Replace("%page%", page).Replace("%uppercat%", cat).Replace("%project%", project).Replace("%response%", result), meta);
    }
    return Results.Content(cpf_template.Replace("%page%", page).Replace("%uppercat%", cat).Replace("%project%", project).Replace("%response%", "<li>Path not found</li>"), meta);
});

app.MapGet("/pages-wo-iwiki", (HttpContext context) =>
{
    var pages = new Dictionary<string, pageinfo_iwiki>(); var FAs = new List<string>(); var GAs = new List<string>(); var RAs = new List<string>(); var FLs = new List<string>();
    var parameters = HttpUtility.ParseQueryString(context.Request.QueryString.ToString());
    if (parameters.Count == 0)
        return Results.Content(iwiki_response("en.wikipedia", "", "", "ru.wikipedia", false, true, false, false, false, 0, 5, ""), meta);
    var sourcewiki = parameters["sourcewiki"]; var category = parameters["category"]; var template = parameters["template"]; var onlyarticles = parameters["pagetype"] == "articles";
    var show_existing_pages = parameters["type"] == "exist"; var targetwiki = parameters["targetwiki"]; var wikilist = parameters["wikilist"] == "on"; var wikitable = parameters["wikitable"] == "on";
    var requireddepth = i(parameters["depth"]); var miniwiki = i(parameters["miniwiki"]); var order_by_status = parameters["sort"] == "status";
    if (requireddepth < 0)
        return Results.Content(iwiki_response(sourcewiki, category, template, targetwiki, show_existing_pages, onlyarticles, order_by_status, wikilist, wikitable, 0, miniwiki, "Enter non-negative depth"), meta);
    if (category == "" && template == "")
        return Results.Content(iwiki_response(sourcewiki, category, template, targetwiki, show_existing_pages, onlyarticles, order_by_status, wikilist, wikitable, requireddepth, miniwiki, "Enter category, template or both"), meta);
    var targetpages = new Dictionary<string, pageinfo_iwiki>(); var existentpageids = new List<int>(); var site = login(sourcewiki, creds[0], creds[1], creds[3]);

    if (category != "")
        searchsubcats_iwiki(category, 0, sourcewiki, site, onlyarticles, pages, requireddepth);

    if (template != "") {
        string nstag = (onlyarticles ? "&einamespace=0" : "");
        string cont = "", query = "https://" + sourcewiki + ".org/w/api.php?action=query&format=xml&list=embeddedin&eititle=" + Uri.EscapeDataString(template) + nstag + "&eilimit=max";
        while (cont != null) {
            var r = new XmlTextReader(new StringReader(cont == "" ? site.GetStringAsync(query).Result : site.GetStringAsync(query + "&eicontinue=" + Uri.EscapeDataString(cont)).Result));
            r.Read(); r.Read(); r.Read(); cont = r.GetAttribute("eicontinue");
            while (r.Read())
                if (r.NodeType == XmlNodeType.Element && r.Name == "ei")
                    if (!pages.ContainsKey(r.GetAttribute("title"))) { pages.Add(r.GetAttribute("title"), new pageinfo_iwiki()); }
        }
    }
    if (pages.Count == 0)
        return Results.Content(iwiki_response(sourcewiki, category, template, targetwiki, show_existing_pages, onlyarticles, order_by_status, wikilist, wikitable, requireddepth, miniwiki, "There are no pages in this category or using this template"), meta);
    else {
        gather_quality_pages(FAs, "Q5626124", site, show_existing_pages ? targetwiki : sourcewiki); gather_quality_pages(GAs, "Q5303", site, show_existing_pages ? targetwiki : sourcewiki);
        gather_quality_pages(FLs, "Q5857568", site, show_existing_pages ? targetwiki : sourcewiki); gather_quality_pages(RAs, "Q13402307", site, show_existing_pages ? targetwiki : sourcewiki);
        var connect = new MySqlConnection(creds[2].Replace("%project%", "wikidatawiki")); connect.Open();
        foreach (var pagename_on_sourcewiki in pages.Keys) {
            int numofiwiki = 0, itemid;
            MySqlDataReader rd = new MySqlCommand("select ips_item_id from wb_items_per_site where ips_site_id=\"" + url2db(sourcewiki) + "\" and ips_site_page=\"" + pagename_on_sourcewiki.Replace("\"",
                "\\\"") + "\";", connect).ExecuteReader();
            if (rd.Read()) { itemid = rd.GetInt32(0); pages[pagename_on_sourcewiki].id = itemid; rd.Close(); }
            else { rd.Close(); continue; }
            rd = new MySqlCommand("select count(*) c from wb_items_per_site where ips_item_id=\"" + itemid + "\";", connect).ExecuteReader(); rd.Read(); numofiwiki = rd.GetInt32(0);
            pages[pagename_on_sourcewiki].numofiwiki = numofiwiki; rd.Close();
            pages[pagename_on_sourcewiki].status = GetStatusOnRequestedWiki(pagename_on_sourcewiki, FAs, GAs, RAs, FLs);
            if (show_existing_pages) {
                rd = new MySqlCommand("select cast(ips_site_page as char) from wb_items_per_site where ips_site_id=\"" + url2db(targetwiki) + "\" and ips_item_id=\"" + itemid + "\";", connect).ExecuteReader();
                if (rd.Read()) {
                    string pagename_on_targetwiki = rd.GetString(0);
                    targetpages.Add(pagename_on_targetwiki, new pageinfo_iwiki() { numofiwiki = numofiwiki, status = GetStatusOnRequestedWiki(pagename_on_targetwiki, FAs, GAs, RAs, FLs) });
                } rd.Close();
            }
            else {
                rd = new MySqlCommand("select cast(ips_site_page as char) from wb_items_per_site where ips_site_id=\"" + url2db(targetwiki) + "\" and ips_item_id=\"" + itemid + "\";", connect).ExecuteReader();
                if (rd.Read())
                    existentpageids.Add(itemid);
                rd.Close();
            }
        }
        string result = "<table border=\"1\" cellspacing=\"0\"><tr><th>Page</th><th># of interwikis</th><th>Status</th></tr>\n";
        if (show_existing_pages) {
            foreach (var p in order_by_status ? targetpages.OrderByDescending(p => p.Value.status) : targetpages.OrderByDescending(p => p.Value.numofiwiki))
                if (p.Value.numofiwiki >= miniwiki)
                    result += "<tr><td><a href=\"https://" + targetwiki + ".org/wiki/" + Uri.EscapeDataString(p.Key) + "\">" + p.Key + "</a></td><td>" + p.Value.numofiwiki + "</td><td>" + p.Value.status + "</td></tr>\n";
        } else {
            foreach (var p in order_by_status ? pages.OrderByDescending(p => p.Value.status) : pages.OrderByDescending(p => p.Value.numofiwiki))
                if (!existentpageids.Contains(p.Value.id) && p.Value.numofiwiki >= miniwiki)
                    result += "<tr><td><a href=\"https://" + sourcewiki + ".org/wiki/" + Uri.EscapeDataString(p.Key) + "\">" + p.Key + "</a></td><td>" + p.Value.numofiwiki + "</td><td>" + p.Value.status + "</td></tr>\n";
        }
        result += "</table></center>";
        if (wikilist && !show_existing_pages)
            foreach (var p in order_by_status ? pages.OrderByDescending(p => p.Value.status) : pages.OrderByDescending(p => p.Value.numofiwiki))
                if (!existentpageids.Contains(p.Value.id) && p.Value.numofiwiki >= miniwiki)
                    result += "\n<br>#{{iw|||" + sourcewiki.Substring(0, sourcewiki.IndexOf('.')) + "|" + p.Key + "}}";
        if (wikitable) {
            result += "\n<br>{|class=\"standard sortable\"<br>! Страница !! Интервик !! Статус";
            foreach (var p in order_by_status ? pages.OrderByDescending(p => p.Value.status) : pages.OrderByDescending(p => p.Value.numofiwiki))
                if (!existentpageids.Contains(p.Value.id) && p.Value.numofiwiki >= miniwiki)
                    result += "<br>|-<br>| [[:" + sourcewiki.Substring(0, sourcewiki.IndexOf('.')) + ":" + p.Key + "|]] || " + p.Value.numofiwiki + " || " + p.Value.status;
            result += "<br>|}";
        }
        return Results.Content(iwiki_response(sourcewiki, category, template, targetwiki, show_existing_pages, onlyarticles, order_by_status, wikilist, wikitable, requireddepth, miniwiki, result), meta);
    }
});

app.MapGet("/page-authors", (HttpContext context) =>
{
    int c = 0; var stats = new page_authors_stats() { list = new Dictionary<string, int>() }; var prms = HttpUtility.ParseQueryString(context.Request.QueryString.ToString());
    if (prms.Count == 0)
        return Results.Content(authors_response("cat", "ru.wikipedia", "", 2, "", 0), meta);
    var type = prms["type"]; var project = prms["wiki"]; var rawsource = prms["source"];var min_num_of_pages = i(prms["min_num_of_pages"]); var depth = i(prms["depth"]);
    var pages = new page_authors_stats() { list = new Dictionary<string, int>() };
    var site = login(project, creds[0], creds[1], creds[3]); var source = rawsource.Replace(" ", "_").Replace("\u200E", "").Replace("\r", "").Split('\n');//удаляем пробел нулевой ширины
    var connect = new MySqlConnection(creds[2].Replace("%project%", project.Replace(".", "").Replace("wikipedia", "wiki"))); connect.Open(); MySqlCommand command; MySqlDataReader r;
    foreach (var s in source) {
        string upcased = char.ToUpper(s[0]) + s.Substring(1);
        if (type == "cat")
            searchsubcats(upcased, 0, depth, site, project, pages);
        else if (type == "tmplt") {
            command = new MySqlCommand("select tl_from from templatelinks join linktarget on lt_id=tl_target_id where lt_title=\"" + upcased + "\" and lt_namespace=10;", connect) { CommandTimeout = 99999 };
            r = command.ExecuteReader();
            while (r.Read())
                pages.list.Add(r.GetInt32(0).ToString(), 0);
            r.Close();
        }
        else if (type == "talkcat") {
            command = new MySqlCommand("select cast(page_title as char) title from categorylinks join page on page_id=cl_from where cl_to=\"" + upcased + "\";", connect) { CommandTimeout = 99999 };
            r = command.ExecuteReader();
            while (r.Read())
                pages.list.Add(r.GetString(0), 0);
            r.Close();
        }
        else if (type == "talktmplt") {
            command = new MySqlCommand("select cast(page_title as char) title from templatelinks join page on page_id=tl_from join linktarget on lt_id=tl_target_id where lt_title=\"" + upcased + "\" and lt_namespace=10;", connect) { CommandTimeout = 99999 };
            r = command.ExecuteReader();
            while (r.Read())
                pages.list.Add(r.GetString(0), 0);
            r.Close();
        }
        else if (type == "links") {
            string cont = "", query = "https://ru.wikipedia.org/w/api.php?action=query&format=xml&prop=links&titles=" + upcased + "&pllimit=max";
            while (cont != null) {
                var xr = new XmlTextReader(new StringReader(cont == "" ? site.GetStringAsync(query).Result : site.GetStringAsync(query + "&plcontinue=" + cont).Result));
                xr.Read(); xr.Read(); xr.Read(); cont = xr.GetAttribute("plcontinue");
                while (xr.Read())
                    if (xr.Name == "pl")
                        pages.list.Add(xr.GetAttribute("title").Replace(" ", "_"), 0);
            }
        }
    }
    if (type == "cat" || type == "tmplt")
        foreach (var id in pages.list.Keys)
            get_first_author("https://" + project + ".org/w/api.php?action=query&format=xml&prop=revisions&rvprop=user&rvlimit=1&rvdir=newer&pageids=" + id, site, stats);

    else if (type == "talkcat" || type == "talktmplt" || type == "links")
        foreach (var name in pages.list.Keys)
            get_first_author("https://" + project + ".org/w/api.php?action=query&format=xml&prop=revisions&rvprop=user&rvlimit=1&rvdir=newer&titles=" + Uri.EscapeDataString(name), site, stats);

    string result = "Total pages: " + pages.list.Count + "." + (stats.hidden > 0 ? " Author is hidden on " + stats.hidden + " pages." : "") +
    (stats.error > 0 ? " Can't get author on " + stats.error + " pages." : "") + "<br><br><table border=\"1\" cellspacing=\"0\"><tr><th>№</th><th>User</th><th>Created pages</th></tr>\n";
    foreach (var u in stats.list.OrderByDescending(u => u.Value)) {
        if (u.Value < min_num_of_pages)
            break;
        result += "<tr><td>" + ++c + "</td><td><a href=\"https://" + project + ".org/wiki/User:" + Uri.EscapeDataString(u.Key) + "\">" + u.Key + "</a></td><td>" + u.Value + "</td></tr>\n";
    }
    return Results.Content(authors_response("cat", "ru.wikipedia", "", 2, "", depth), meta);    
});
app.Run();
HttpClient login(string project, string login, string password, string ua) {
    var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, UseCookies = true, CookieContainer = new CookieContainer() }); client.DefaultRequestHeaders.Add("User-Agent", ua);
    var result = client.GetAsync("https://" + project + ".org/w/api.php?action=query&meta=tokens&type=login&format=xml").Result; var doc = new XmlDocument(); doc.LoadXml(result.Content
        .ReadAsStringAsync().Result); var logintoken = doc.SelectSingleNode("//tokens/@logintoken").Value; result = client.PostAsync("https://" + project + ".org/w/api.php", new
            FormUrlEncodedContent(new Dictionary<string, string> { { "action", "login" }, { "lgname", login }, { "lgpassword", password }, { "lgtoken", logintoken }, { "format", "xml" } })).Result; return client;
}
string url2db(string url) { return url.Replace(".", "").Replace("wikipedia", "wiki"); } int i(Object input) { return Convert.ToInt32(input); }
void searchsubcats(string category, int currentdepth, int requireddepth, HttpClient site, string wiki, page_authors_stats pages) {
    string cont = "", query = "https://" + wiki + ".org/w/api.php?action=query&list=categorymembers&format=xml&cmtitle=Category:" + Uri.EscapeDataString(category) + "&cmprop=title&cmlimit=max";
    while (cont != null) {
        var r = new XmlTextReader(new StringReader(cont == "" ? site.GetStringAsync(query).Result : site.GetStringAsync(query + "&cmcontinue=" + Uri.EscapeDataString(cont)).Result));
        r.Read(); r.Read(); r.Read(); cont = r.GetAttribute("cmcontinue");
        while (r.Read())
            if (r.Name == "cm")
                if (!pages.list.ContainsKey(r.GetAttribute("title")))
                    pages.list.Add(r.GetAttribute("title"), 0);
    }
    cont = "";
    query = "https://" + wiki + ".org/w/api.php?action=query&list=categorymembers&format=xml&cmtitle=Category:" + Uri.EscapeDataString(category) + "&cmnamespace=14&cmprop=title&cmlimit=max";
    while (cont != null) {
        var r = new XmlTextReader(new StringReader(cont == "" ? site.GetStringAsync(query).Result : site.GetStringAsync(query + "&cmcontinue=" + Uri.EscapeDataString(cont)).Result));
        r.Read(); r.Read(); r.Read(); cont = r.GetAttribute("cmcontinue");
        while (r.Read())
            if (r.Name == "cm") {
                string fullcategoryname = r.GetAttribute("title"); string shortcategoryname = fullcategoryname.Substring(fullcategoryname.IndexOf(':') + 1);
                if (currentdepth + 1 <= requireddepth)
                    searchsubcats(shortcategoryname, currentdepth + 1, requireddepth, site, wiki, pages);
            }
    }
}
void searchsubcats_unreviewed(string category, int currentdepth, int requireddepth, HttpClient site, string wiki, HashSet<string> candidates, bool talks)
{
    string cont = "", query = "https://" + wiki + ".org/w/api.php?action=query&list=categorymembers&format=xml&cmtitle=Category:" + Uri.EscapeDataString(category) + "&cmprop=" + (talks ? "title" : "ids") + "&cmlimit=max" + (talks ? "" : "&cmnamespace=100|102|0|6|10|14");
    while (cont != null) {
        var r = new XmlTextReader(new StringReader(cont == "" ? site.GetStringAsync(query).Result : site.GetStringAsync(query + "&cmcontinue=" + Uri.EscapeDataString(cont)).Result));
        r.Read(); r.Read(); r.Read(); cont = r.GetAttribute("cmcontinue");
        while (r.Read())
            if (r.Name == "cm") {
                string id_or_title = r.GetAttribute(talks ? "title" : "pageid");
                if (talks && id_or_title.Contains(':'))
                    id_or_title = id_or_title.Substring(id_or_title.IndexOf(':') + 1);
                candidates.Add(id_or_title);
            }
    }
    cont = ""; //собираем категории
    query = "https://" + wiki + ".org/w/api.php?action=query&list=categorymembers&format=xml&cmtitle=Category:" + Uri.EscapeDataString(category) + "&cmnamespace=14&cmprop=title&cmlimit=max";
    while (cont != null) {
        var r = new XmlTextReader(new StringReader(cont == "" ? site.GetStringAsync(query).Result : site.GetStringAsync(query + "&cmcontinue=" + Uri.EscapeDataString(cont)).Result));
        r.Read(); r.Read(); r.Read(); cont = r.GetAttribute("cmcontinue");
        while (r.Read())
            if (r.Name == "cm") {
                string fullcategoryname = r.GetAttribute("title"); string shortcategoryname = fullcategoryname.Substring(fullcategoryname.IndexOf(':') + 1);
                if (currentdepth + 1 <= requireddepth)
                    searchsubcats_unreviewed(shortcategoryname, currentdepth + 1, requireddepth, site, wiki, candidates, talks);
            }
    }
}
void searchsubcats_iwiki(string category, int currentdepth, string sourcewiki, HttpClient site, bool onlyarticles, Dictionary<string, pageinfo_iwiki> pages, int requireddepth) {
    string nstag = onlyarticles ? "&cmnamespace=0" : ""; //собираем страницы
    string cont = "", query = "https://" + sourcewiki + ".org/w/api.php?action=query&list=categorymembers&format=xml&cmtitle=Category:" + Uri.EscapeDataString(category) + nstag + "&cmprop=title&cmlimit=max";
    while (cont != null) {
        var r = new XmlTextReader(new StringReader(cont == "" ? site.GetStringAsync(query).Result : site.GetStringAsync(query + "&cmcontinue=" + Uri.EscapeDataString(cont)).Result));
        r.Read(); r.Read(); r.Read(); cont = r.GetAttribute("cmcontinue"); while (r.Read())
            if (r.NodeType == XmlNodeType.Element && r.Name == "cm")
                if (!pages.ContainsKey(r.GetAttribute("title"))) { pages.Add(r.GetAttribute("title"), new pageinfo_iwiki()); }
    }
    cont = ""; query = "https://" + sourcewiki + ".org/w/api.php?action=query&list=categorymembers&format=xml&cmtitle=Category:" + Uri.EscapeDataString(category) + "&cmnamespace=14&cmprop=title&cmlimit=max";
    while (cont != null) {
        var r = new XmlTextReader(new StringReader(cont == "" ? site.GetStringAsync(query).Result : site.GetStringAsync(query + "&cmcontinue=" + Uri.EscapeDataString(cont)).Result));
        r.Read(); r.Read(); r.Read(); cont = r.GetAttribute("cmcontinue");
        while (r.Read())
            if (r.NodeType == XmlNodeType.Element && r.Name == "cm") {
                string fullcategoryname = r.GetAttribute("title"); string shortcategoryname = fullcategoryname.Substring(fullcategoryname.IndexOf(':') + 1);
                if (currentdepth + 1 <= requireddepth)
                    searchsubcats_iwiki(shortcategoryname, currentdepth + 1, sourcewiki, site, onlyarticles, pages, requireddepth);
            }
    }
}
void put_new_action(string user, string type, int ns, Dictionary<string, stat> usertable) {
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
string patstats_response(string type, string project, string startdate, string enddate, string sort, string answer, string html_template)
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
<label><input type=""radio"" name=""sort"" value=""unpat"" %checked_unpat%>unreviews</label>").Replace("%result%", answer).Replace("%project%", project).Replace("%startdate%", startdate).Replace("%enddate%",
enddate).Replace(type == "db" ? "%checked_db%" : "%checked_api%", "checked");
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
string unreviewed_response(string wiki, string cat, string template, int depth, string answer, bool talks, string html_template)
{
    string title = "";
    if (cat != "" && template != "")
        title = " (" + cat + ", " + template + ")";
    else if (cat != "")
        title = " (" + cat + ")";
    else if (template != "")
        title = " (" + template + ")";
    string resulttext = html_template.Replace("%title%", "Unreviewed " + title).Replace("%form%", "unreviewed-pages").Replace("%body%",
        @"Pages in <input type=""text"" name=""wiki"" value=""%wiki%"" size=""11"" required> From category <input type=""text"" name=""cat"" value=""%cat%"" placeholder=""without Category: prefix"">
<label>Article talk pages category <input type=""checkbox"" name=""talks"" %checked_talks%></label> with subcats to depth <input type=""number"" name=""depth"" value=""%depth%"" style=""width:2em"">
<br><br>OR using template <input type=""text"" name=""template"" value=""%template%"" placeholder=""with Template: prefix"">").Replace("%result%", answer).Replace("%wiki%", wiki).Replace("%cat%", cat)
.Replace("%depth%", depth.ToString()).Replace("%template%", template);
    if (talks)
        resulttext = resulttext.Replace("%checked_talks%", "checked");
    return resulttext;
}
string iwiki_response(string sourcewiki, string category, string template, string targetwiki, bool show_existing_pages, bool onlyarticles, bool order_by_status, bool wikilist, bool wikitable, int depth, int miniwiki, string answer)
{
    string result = html_template.Replace("%title%", "Pages without interwiki%title%").Replace("%form%", "pages-wo-iwiki").Replace("%result%", answer).Replace("%body%", @"
Pages in <input type=""text"" name=""sourcewiki"" value=""%sourcewiki%"" size=""11"" required>
From category <input type=""text"" name=""category"" value=""%category%"" placeholder=""without Category: prefix"">
with subcats to depth <input type=""number"" name=""depth"" value=""%depth%"" style=""width:2em"">
Using template <input type=""text"" name=""template"" value=""%template%"" placeholder=""with Template: prefix""><br><br>
Show <select size=""1"" name=""pagetype""><option value=""articles"" %selected_articles%>articles</option><option value=""allpages"" %selected_allpages%>all pages</option></select>
<select size=""1"" name=""type""><option value=""nonexist"" %selected_nonexist%>without interwiki link</option><option value=""exist"" %selected_exist%>with interwiki link</option></select>
 to <input type=""text"" name=""targetwiki"" value=""%targetwiki%"" size=""11"" required>
having not less than <input type=""number"" name=""miniwiki"" value=""%miniwiki%"" style=""width:3em""> interwiki links<br><br>
Order by <select size=""1"" name=""sort""><option value=""iwiki"" %selected_iwiki%>number of interwiki links</option><option value=""status"" %selected_status%>featured status</option></select>
Generate a list<label><input type=""checkbox"" name=""wikilist"" %checked_wikilist%> {{iw}} templates</label>
<label><input type=""checkbox"" name=""wikitable"" %checked_wikitable%> wikitable</label>").Replace("%sourcewiki%", sourcewiki).Replace("%category%", category).Replace("%template%", template).Replace(
        "%targetwiki%", targetwiki).Replace("%depth%", depth.ToString()).Replace("%miniwiki%", miniwiki.ToString()).Replace(show_existing_pages ? "%selected_exist%" : "%selected_nonexist%", "selected")
        .Replace(onlyarticles ? "%selected_articles%" : "%selected_allpages%", "selected").Replace(order_by_status ? "%selected_status%" : "%selected_iwiki%" , "selected");
    if (wikilist)
        result = result.Replace("%checked_wikilist%", "checked");
    if (wikitable)
        result = result.Replace("%checked_wikitable%", "checked");
    string title = "";
    if (category != "" && template != "")
        title = " (" + category + ", " + template + ")";
    else if (category != "")
        title = " (" + category + ")";
    else if (template != "")
        title = " (" + template + ")";
    return result.Replace("%title%", title);
}
string authors_response(string type, string project, string source, int min_num_of_pages, string answer, int depth)
{
    string result = html_template.Replace("%title%", "Page authors stats").Replace("%form%", "page-authors").Replace("%wiki%", project).Replace("%result%", answer).Replace("%source%", source).Replace("%body%",
        @"In <input type=""text"" name=""wiki"" value=""%wiki%"" required> get authors of pages 
<label><input type=""radio"" name=""type"" value=""cat"" required %checked_cat%>from category</label>
<label><input type=""radio"" name=""type"" value=""tmplt"" required %checked_tmplt%>using template</label><br>
<label><input type=""radio"" name=""type"" value=""links"" required %checked_links%>listed on a page</label>
<label><input type=""radio"" name=""type"" value=""talktmplt"" required %checked_talktmplt%>using talk template</label>
<label><input type=""radio"" name=""type"" value=""talkcat"" required %checked_talkcat%>with talk category</label><br>
 with subcats to depth <input type=""number"" name=""depth"" value=""%depth%"" style=""width:2em"">
List of categories/templates/page lists:<textarea name=""source"" placeholder=""One title per line, without 'Template'/'Category' prefixes"" wrap=""soft"" rows=""5"" cols=""60"" required>%source%</textarea>
<br><br>Show only authors of not less than <input type=""number"" name=""min_num_of_pages"" value=""%min_num_of_pages%"" style=""width:3em"" required> pages").Replace("%depth%", depth.ToString())
.Replace("%min_num_of_pages%", min_num_of_pages.ToString());
    if (type == "cat")
        result = result.Replace("%checked_cat%", "checked");
    else if (type == "tmplt")
        result = result.Replace("%checked_tmplt%", "checked");
    else if (type == "talktmplt")
        result = result.Replace("%checked_talktmplt%", "checked");
    else if (type == "links")
        result = result.Replace("%checked_links%", "checked");
    else if (type == "talkcat")
        result = result.Replace("%checked_talkcat%", "checked");
    return result;
}
catpath search_upcats(string project, string purpose_cat, string currentcat, catpath path, HashSet<string> processedcats, HttpClient site) {
    processedcats.Add(currentcat);
    var upcats = new List<string>();
    using (var r = new XmlTextReader(new StringReader(site.GetStringAsync("https://" + project + ".org/w/api.php?action=query&prop=categories&format=xml&cllimit=max&titles=" + Uri.EscapeDataString(currentcat)).Result)))
        while (r.Read())
            if (r.Name == "cl")
                upcats.Add(r.GetAttribute("title"));
    if (upcats.Contains(purpose_cat)) { path.path.Add(purpose_cat); path.path.Add(currentcat); path.found = true; return path; }
    foreach (string upcat in upcats)
        if (!processedcats.Contains(upcat) && !path.found)
            path = search_upcats(project, purpose_cat, upcat, path, processedcats, site);
    if (path.found) { path.path.Add(currentcat); return path; }
    return path;
}
void gather_quality_pages(List<string> list_of_quality_pages, string wd_item, HttpClient site, string requestedwiki) {
    string quality_template_name = "";
    using (var r = new XmlTextReader(new StringReader(site.GetStringAsync("https://www.wikidata.org/w/api.php?action=wbgetentities&format=xml&ids=" + wd_item + "&props=sitelinks").Result)))
        while (r.Read())
            if (r.Name == "sitelink" && r.GetAttribute("site") == url2db(requestedwiki))
                quality_template_name = r.GetAttribute("title");
    if (quality_template_name != "") {
        string cont = "", query = "https://" + requestedwiki + ".org/w/api.php?action=query&format=json&formatversion=2&list=embeddedin&eititle=" + Uri.EscapeDataString(quality_template_name) + "&eilimit=max";
        while (cont != "-") {
            Root response = JsonConvert.DeserializeObject<Root>(cont == "" ? site.GetStringAsync(query).Result : site.GetStringAsync(query + "&eicontinue=" + Uri.EscapeDataString(cont)).Result);
            if (response.@continue != null)
                cont = response.@continue.eicontinue;
            else
                cont = "-";
            foreach (var name in response.query.embeddedin)
                list_of_quality_pages.Add(name.title);
        }
    }
}
string GetStatusOnRequestedWiki(string page, List<string> FAs, List<string> GAs, List<string> RAs, List<string> FLs) {
    string status = "";
    if (FAs.Contains(page))
        status = "<abbr title=\"Featured article\">🥇</abbr>";
    else if (GAs.Contains(page))
        status = "<abbr title=\"Good article\">🥈</abbr>";
    else if (RAs.Contains(page))
        status = "<abbr title=\"Recommended article\">🥉</abbr>";
    else if (FLs.Contains(page))
        status = "<abbr title=\"Featured list\">📜</abbr>";
    return status;
}
void get_first_author(string request, HttpClient site, page_authors_stats stats) {
    try//обращение к БД гораздо медленнее
    {
        var r = new XmlTextReader(new StringReader(site.GetStringAsync(request).Result));
        while (r.Read())
            if (r.Name == "rev") {
                if (r.GetAttribute("userhidden") == "")
                    stats.hidden++;
                else {
                    string user = r.GetAttribute("user");
                    if (stats.list.ContainsKey(user))
                        stats.list[user]++;
                    else stats.list.Add(user, 1);
                }
            }
    }
    catch { stats.error++; }
}
class page { public required string title; public int oldsize, newsize; public float times; }
class stat { public int main, template, cat, file, portal, unpat, module, sum; }
class pageinfo_oldreviewed { public string pending_since, stable_revid; }
class catpath { public List<string> path = new List<string>(); public bool found; }
class pageinfo_iwiki { public string status; public int numofiwiki, id; }
class page_authors_stats { public Dictionary<string, int> list; public int hidden, error; }
class Continue { public string eicontinue; public string @continue; }
class Embeddedin { public int pageid; public int ns; public string title; }
class Query { public List<Embeddedin> embeddedin; }
class Root { public bool batchcomplete; public Continue @continue; public Query query; }
