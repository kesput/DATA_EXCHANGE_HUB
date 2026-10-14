using System.Globalization;
using System.Text;
using System.Text.Json;
using DATA_EXCHANGE_HUB.Models;
using DATA_EXCHANGE_HUB.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace DATA_EXCHANGE_HUB.Pages.Sphinx;

public sealed class IndexModel(
    ISphinxService sphinxService,
    IUserNameResolver userNameResolver,
    IExcelWorkbookService excelWorkbookService,
    IStringLocalizer<DATA_EXCHANGE_HUB.SharedResource> localizer) : PageModel
{
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private static readonly string[] TemplateHeaders =
    [
        "Order",
        "ParentOptionKey",
        "QuestionKey",
        "QuestionText",
        "Type",
        "TextValidation",
        "OptionKey",
        "OptionText",
        "SkipAction",
        "SkipToQuestionKey"
    ];

    [BindProperty]
    public string SphinxSurveyTitle { get; set; } = string.Empty;

    [BindProperty]
    public string? SphinxSurveyDescription { get; set; }

    [BindProperty]
    public string SphinxCreatedBy { get; set; } = string.Empty;

    [BindProperty]
    public string SphinxDefinitionJson { get; set; } = string.Empty;

    [BindProperty]
    public string SurveyKind { get; set; } = "SURVEY";

    [BindProperty]
    public string LimitMode { get; set; } = "none";

    [BindProperty]
    public int? MaxResponses { get; set; }

    [BindProperty]
    public string? ExpiresAtString { get; set; }

    [BindProperty]
    public bool IsAnonymous { get; set; } = true;

    [BindProperty]
    public IFormFile? ImportFile { get; set; }

    public IReadOnlyList<SphinxManagementItem> Surveys { get; private set; } = [];
    public SphinxAccessState Access { get; private set; } = new(false, false);

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        SphinxCreatedBy = userNameResolver.ResolveUserName(User);
        Access = await GetAccessAsync();
        if (!Access.CanOpen)
        {
            return Forbid();
        }

        await LoadSurveysAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        SphinxCreatedBy = userNameResolver.ResolveUserName(User);
        Access = await GetAccessAsync();
        if (!Access.CanOpen)
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(SphinxSurveyTitle))
        {
            ErrorMessage = localizer["Sphinx.Error.TitleRequired"];
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(SphinxDefinitionJson))
        {
            ErrorMessage = localizer["Sphinx.Error.QuestionRequired"];
            return RedirectToPage();
        }

        int? maxResp = null;
        DateTimeOffset? expiresAt = null;

        if (LimitMode == "count" && MaxResponses.HasValue && MaxResponses.Value > 0)
        {
            maxResp = MaxResponses.Value;
        }

        if (LimitMode == "date" && !string.IsNullOrWhiteSpace(ExpiresAtString) &&
            DateTimeOffset.TryParse(ExpiresAtString, out var parsedDate))
        {
            expiresAt = parsedDate;
        }

        try
        {
            await sphinxService.CreateSurveyAsync(
                new SphinxCreateSurveyRequest(
                    SphinxSurveyTitle.Trim(),
                    SphinxSurveyDescription?.Trim(),
                    SphinxCreatedBy,
                    SphinxDefinitionJson,
                    maxResp,
                    expiresAt,
                    IsAnonymous,
                    SurveyKind),
                HttpContext.RequestAborted);

            SuccessMessage = localizer["Sphinx.Success.Created"];
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCloseAsync(int id)
    {
        var currentUserName = userNameResolver.ResolveUserName(User);
        Access = await GetAccessAsync();
        if (!Access.CanOpen)
        {
            return Forbid();
        }

        try
        {
            await sphinxService.CloseSurveyAsync(id, currentUserName, Access.IsAdmin, HttpContext.RequestAborted);
            SuccessMessage = localizer["Sphinx.Success.Closed"];
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnGetResultsAsync(int id)
    {
        var currentUserName = userNameResolver.ResolveUserName(User);
        Access = await GetAccessAsync();
        if (!Access.CanOpen)
        {
            return Forbid();
        }

        var export = await sphinxService.GetResultsExportAsync(
            id,
            currentUserName,
            Access.IsAdmin,
            HttpContext.RequestAborted);

        if (export is null)
        {
            return Forbid();
        }

        var file = excelWorkbookService.CreateWorkbook(
            export.Headers,
            export.Rows,
            "SPHINX results");

        return File(file, ExcelContentType, export.FileName);
    }

    public async Task<IActionResult> OnGetExportTemplateAsync()
    {
        Access = await GetAccessAsync();
        if (!Access.CanOpen)
        {
            return Forbid();
        }

        var rows = CreateTemplateRows();
        var file = excelWorkbookService.CreateWorkbook(TemplateHeaders, rows, "SPHINX template");

        return File(file, ExcelContentType, "sphinx_apklausos_template.xlsx");
    }

    public async Task<IActionResult> OnGetExportSurveyTemplateAsync(int id)
    {
        var currentUserName = userNameResolver.ResolveUserName(User);
        Access = await GetAccessAsync();
        if (!Access.CanOpen)
        {
            return Forbid();
        }

        var survey = await sphinxService.GetSurveyForOwnerAsync(
            id,
            currentUserName,
            Access.IsAdmin,
            HttpContext.RequestAborted);
        if (survey is null)
        {
            return Forbid();
        }

        var rows = CreateRowsFromSurvey(survey.TopLevelQuestions);
        var file = excelWorkbookService.CreateWorkbook(TemplateHeaders, rows, "SPHINX export");
        var safeTitle = new string(survey.Survey.Title.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray()).Trim('_');
        if (string.IsNullOrWhiteSpace(safeTitle))
        {
            safeTitle = "apklausa";
        }

        return File(file, ExcelContentType, $"sphinx_{survey.Survey.Id}_{safeTitle}_template.xlsx");
    }

    public async Task<IActionResult> OnPostImportAsync()
    {
        SphinxCreatedBy = userNameResolver.ResolveUserName(User);
        Access = await GetAccessAsync();
        if (!Access.CanOpen)
        {
            return Forbid();
        }

        if (ImportFile is null || ImportFile.Length == 0)
        {
            ErrorMessage = localizer["Sphinx.Error.SelectExcelFile"];
            return RedirectToPage();
        }

        if (!ExcelFileNames.IsSupportedUpload(ImportFile.FileName))
        {
            ErrorMessage = localizer["Sphinx.Error.InvalidExcelFile"];
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(SphinxSurveyTitle))
        {
            ErrorMessage = localizer["Sphinx.Error.ImportTitleRequired"];
            return RedirectToPage();
        }

        int? maxResp = null;
        DateTimeOffset? expiresAt = null;
        if (LimitMode == "count" && MaxResponses.HasValue && MaxResponses.Value > 0)
        {
            maxResp = MaxResponses.Value;
        }
        if (LimitMode == "date" && !string.IsNullOrWhiteSpace(ExpiresAtString) &&
            DateTimeOffset.TryParse(ExpiresAtString, out var parsedDate))
        {
            expiresAt = parsedDate;
        }

        try
        {
            await using var stream = ImportFile.OpenReadStream();
            var worksheet = excelWorkbookService.ReadWorkbook(stream);
            var definitionJson = BuildDefinitionJsonFromWorksheet(worksheet);
            await sphinxService.CreateSurveyAsync(
                new SphinxCreateSurveyRequest(
                    SphinxSurveyTitle.Trim(),
                    SphinxSurveyDescription?.Trim(),
                    SphinxCreatedBy,
                    definitionJson,
                    maxResp,
                    expiresAt,
                    IsAnonymous,
                    SurveyKind),
                HttpContext.RequestAborted);

            SuccessMessage = localizer["Sphinx.Success.Imported"];
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToPage();
    }

    private async Task LoadSurveysAsync()
    {
        try
        {
            Surveys = await sphinxService.GetManagementListAsync(
                userNameResolver.ResolveUserName(User),
                Access.IsAdmin,
                HttpContext.RequestAborted);
        }
        catch
        {
            Surveys = [];
        }
    }

    private async Task<SphinxAccessState> GetAccessAsync()
    {
        try
        {
            return await sphinxService.GetAccessAsync(User, HttpContext.RequestAborted);
        }
        catch
        {
            return new SphinxAccessState(false, false);
        }
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> CreateTemplateRows()
    {
        return
        [
            Row("1", "", "Q1", "Ar darbo aplinka patogi?", "SINGLE", "NONE", "Q1_A1", "Taip", "NEXT", ""),
            Row("1", "", "Q1", "Ar darbo aplinka patogi?", "SINGLE", "NONE", "Q1_A2", "Ne", "NEXT", ""),
            Row("2", "", "Q2", "Kokios priemones jums reikalingos?", "MULTI", "NONE", "Q2_A1", "Ergonomine kede", "NEXT", ""),
            Row("2", "", "Q2", "Kokios priemones jums reikalingos?", "MULTI", "NONE", "Q2_A2", "Papildomas monitorius", "NEXT", ""),
            Row("2", "", "Q2", "Kokios priemones jums reikalingos?", "MULTI", "NONE", "Q2_A3", "Tylesne darbo vieta", "NEXT", ""),
            Row("3", "", "Q3", "Ar norite palikti komentara?", "SINGLE", "NONE", "Q3_A1", "Taip", "NEXT", ""),
            Row("3", "", "Q3", "Ar norite palikti komentara?", "SINGLE", "NONE", "Q3_A2", "Ne", "END", ""),
            Row("1", "Q3_A1", "Q3_SUB1", "Irasykite komentara", "TEXT", "NONE", "", "", "NEXT", ""),
            Row("4", "", "Q4", "Kontaktinis el. pastas", "TEXT", "EMAIL", "", "", "NEXT", ""),
            Row("5", "", "Q5", "Kontaktinis telefonas", "TEXT", "PHONE", "", "", "NEXT", ""),
            Row("6", "", "Q6", "Kiek metu dirbate imoneje?", "TEXT", "NUMBER", "", "", "NEXT", "")
        ];
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> CreateRowsFromSurvey(
        IReadOnlyList<SphinxQuestionNode> questions)
    {
        var rows = new List<IReadOnlyDictionary<string, string?>>();
        var questionKeys = new Dictionary<int, string>();
        var topIndex = 1;

        foreach (var question in questions.OrderBy(question => question.Order))
        {
            AppendQuestionRows(
                question,
                topIndex.ToString(CultureInfo.InvariantCulture),
                string.Empty,
                $"Q{topIndex}",
                rows,
                questionKeys);
            topIndex++;
        }

        foreach (var row in rows.OfType<Dictionary<string, string?>>())
        {
            if (row.TryGetValue("SkipToQuestionKey", out var skipTo) &&
                int.TryParse(skipTo, NumberStyles.Integer, CultureInfo.InvariantCulture, out var skipQuestionId) &&
                questionKeys.TryGetValue(skipQuestionId, out var skipQuestionKey))
            {
                row["SkipToQuestionKey"] = skipQuestionKey;
            }
        }

        return rows;
    }

    private static void AppendQuestionRows(
        SphinxQuestionNode question,
        string order,
        string parentOptionKey,
        string questionKey,
        List<IReadOnlyDictionary<string, string?>> rows,
        Dictionary<int, string> questionKeys)
    {
        questionKeys[question.Id] = questionKey;
        if (question.Type.Equals("TEXT", StringComparison.OrdinalIgnoreCase) || question.Options.Count == 0)
        {
            rows.Add(Row(order, parentOptionKey, questionKey, question.Text, question.Type, question.TextValidation, "", "", "NEXT", ""));
            return;
        }

        var optionIndex = 1;
        foreach (var option in question.Options.OrderBy(option => option.Order))
        {
            var optionKey = $"{questionKey}_A{optionIndex}";
            rows.Add(Row(
                order,
                parentOptionKey,
                questionKey,
                question.Text,
                question.Type,
                question.TextValidation,
                optionKey,
                option.Text,
                option.SkipAction,
                option.SkipToQuestionId?.ToString(CultureInfo.InvariantCulture) ?? ""));

            var subIndex = 1;
            foreach (var subQuestion in option.SubQuestions.OrderBy(sub => sub.Order))
            {
                AppendQuestionRows(
                    subQuestion,
                    subIndex.ToString(CultureInfo.InvariantCulture),
                    optionKey,
                    $"{optionKey}_SUB{subIndex}",
                    rows,
                    questionKeys);
                subIndex++;
            }

            optionIndex++;
        }
    }

    private static string BuildDefinitionJsonFromWorksheet(ExcelWorksheetData worksheet)
    {
        var missingHeaders = TemplateHeaders
            .Where(header => !worksheet.Headers.Contains(header, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (missingHeaders.Length > 0)
        {
            throw new InvalidOperationException($"Excel faile truksta stulpeliu: {string.Join(", ", missingHeaders)}.");
        }

        var questionMap = new Dictionary<string, SphinxQuestionJson>(StringComparer.OrdinalIgnoreCase);
        var optionMap = new Dictionary<string, SphinxOptionJson>(StringComparer.OrdinalIgnoreCase);
        var questionOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var rootQuestionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pendingParents = new List<(string ParentOptionKey, SphinxQuestionJson Question)>();

        foreach (var row in worksheet.Rows)
        {
            var questionKey = GetCell(row, "QuestionKey");
            var questionText = GetCell(row, "QuestionText");
            if (string.IsNullOrWhiteSpace(questionKey) || string.IsNullOrWhiteSpace(questionText))
            {
                continue;
            }

            if (!questionMap.TryGetValue(questionKey, out var question))
            {
                question = new SphinxQuestionJson
                {
                    ClientId = questionKey,
                    Text = questionText,
                    Type = NormalizeQuestionType(GetCell(row, "Type")),
                    TextValidation = NormalizeTextValidation(GetCell(row, "TextValidation"))
                };
                questionMap[questionKey] = question;
                questionOrder[questionKey] = ParseOrder(GetCell(row, "Order"), questionOrder.Count);
            }

            var parentOptionKey = GetCell(row, "ParentOptionKey");
            if (string.IsNullOrWhiteSpace(parentOptionKey))
            {
                rootQuestionKeys.Add(questionKey);
            }
            else
            {
                pendingParents.Add((parentOptionKey, question));
            }

            var optionKey = GetCell(row, "OptionKey");
            var optionText = GetCell(row, "OptionText");
            if (!string.IsNullOrWhiteSpace(optionKey) && !string.IsNullOrWhiteSpace(optionText) &&
                !optionMap.ContainsKey(optionKey))
            {
                var option = new SphinxOptionJson
                {
                    ClientId = optionKey,
                    Text = optionText,
                    SkipAction = NormalizeSkipAction(GetCell(row, "SkipAction")),
                    SkipToId = string.IsNullOrWhiteSpace(GetCell(row, "SkipToQuestionKey")) ? null : GetCell(row, "SkipToQuestionKey")
                };
                optionMap[optionKey] = option;
                question.Options.Add(option);
            }
        }

        foreach (var (parentOptionKey, question) in pendingParents)
        {
            if (optionMap.TryGetValue(parentOptionKey, out var parentOption) &&
                !parentOption.SubQuestions.Contains(question))
            {
                parentOption.SubQuestions.Add(question);
            }
        }

        var definition = new SphinxDefinitionJson
        {
            Questions = rootQuestionKeys
                .Select(key => questionMap[key])
                .OrderBy(question => questionOrder.TryGetValue(question.ClientId, out var order) ? order : int.MaxValue)
                .ToArray()
        };

        if (definition.Questions.Count == 0)
        {
            throw new InvalidOperationException("Excel faile nerasta klausimu.");
        }

        return JsonSerializer.Serialize(definition);
    }

    private static string GetCell(IReadOnlyDictionary<string, string?> row, string key) =>
        row.TryGetValue(key, out var value) ? value?.Trim() ?? string.Empty : string.Empty;

    private static int ParseOrder(string value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var order) ? order : fallback;

    private static string NormalizeQuestionType(string value)
    {
        var normalized = value.Trim().ToUpperInvariant();
        return normalized is "SINGLE" or "MULTI" or "TEXT" ? normalized : "TEXT";
    }

    private static string NormalizeTextValidation(string value)
    {
        var normalized = value.Trim().ToUpperInvariant();
        return normalized is "EMAIL" or "PHONE" or "NUMBER" ? normalized : "NONE";
    }

    private static string NormalizeSkipAction(string value)
    {
        var normalized = value.Trim().ToUpperInvariant();
        return normalized is "END" or "JUMP" ? normalized : "NEXT";
    }

    private static IReadOnlyDictionary<string, string?> Row(
        string order,
        string parentOptionKey,
        string questionKey,
        string questionText,
        string type,
        string textValidation,
        string optionKey,
        string optionText,
        string skipAction,
        string skipToQuestionKey)
    {
        return new Dictionary<string, string?>
        {
            ["Order"] = order,
            ["ParentOptionKey"] = parentOptionKey,
            ["QuestionKey"] = questionKey,
            ["QuestionText"] = questionText,
            ["Type"] = type,
            ["TextValidation"] = textValidation,
            ["OptionKey"] = optionKey,
            ["OptionText"] = optionText,
            ["SkipAction"] = skipAction,
            ["SkipToQuestionKey"] = skipToQuestionKey
        };
    }
}

