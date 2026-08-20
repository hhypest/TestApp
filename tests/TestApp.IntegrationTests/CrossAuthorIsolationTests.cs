using System.Net;
using System.Net.Http.Json;
using TestApp.Api;
using TestApp.Application.Queries;
using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;
using Xunit;

namespace TestApp.IntegrationTests;

/// <summary>
/// Изоляция авторов друг от друга — issue #21 пункт 3, гейт `docs/ROADMAP.md` §7 пункт 3.
/// </summary>
/// <remarks>
/// <para>
/// <c>TestOwnershipTests</c> уже проверяет, что каталог, редактор, ревизии и результаты
/// чужого автора не видны, а администратор видит всё. Здесь закрываются три места, до которых
/// та проверка не доходит.
/// </para>
/// <para>
/// Первое — запись. Владение проверяется в каждом обработчике команды отдельным вызовом
/// <c>TestAccess.EnsureCanManage</c>, то есть двенадцать раз в двенадцати местах. Пропуск в
/// одном из них не сломает ни один существующий тест: чужой тест по-прежнему не виден в
/// каталоге, но окажется изменяемым по прямой ссылке. Поэтому здесь перебираются все
/// мутирующие эндпоинты, а не один представитель.
/// </para>
/// <para>
/// Второе — состояние после отказа. 403 недостаточно: важно, что отказ произошёл до записи,
/// а не после. Поэтому после перебора тест сверяет, что чужой тест не изменился ни в одном
/// поле, которое пытались изменить.
/// </para>
/// <para>
/// Третье — попытки. Эндпоинты попытки ключуются по студенту, а не по владельцу теста.
/// Автор своего же теста не должен читать их: его законное представление — рецензентское
/// <c>/api/v1/results/{attemptId}</c>, где нет выбранных студентом вариантов в исходном виде.
/// </para>
/// </remarks>
public sealed class CrossAuthorIsolationTests
{
    private const string AuthorA = "author-a";
    private const string AuthorB = "author-b";
    private const string AuthorRole = "test-author";

    [Fact]
    public async Task Not_a_single_mutating_endpoint_accepts_a_foreign_author()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        await using var factory = ApiTestHost.Create(database.ConnectionString);
        using var client = factory.CreateClient();

        ApiTestHost.Authenticate(client, AuthorB, AuthorRole);
        var test = await PostValue<TestId>(client, "/api/v1/tests", new CreateTestRequest("Author B original title"), ct);
        var questionId = await PostWithEtag<QuestionId>(client, test, $"/api/v1/tests/{test.Value}/questions",
            new QuestionWriteRequest("Original question", QuestionType.SingleChoice, 1m, 1), ct);
        var optionId = await PostWithEtag<AnswerOptionId>(client, test,
            $"/api/v1/tests/{test.Value}/questions/{questionId.Value}/options",
            new AnswerOptionWriteRequest("Original option", true, 1), ct);

        // ETag читается владельцем и передаётся «атакующему» намеренно: это просто номер
        // версии, угадываемый с первой попытки. Предусловие не является защитой доступа,
        // и тест не должен позволять ему маскировать её отсутствие.
        var etag = await EtagAsync(client, test, ct);

        var mutations = new (HttpMethod Method, string Uri, object? Body)[]
        {
            (HttpMethod.Patch, $"/api/v1/tests/{test.Value}/title", new RenameTestRequest("Hijacked title")),
            (HttpMethod.Patch, $"/api/v1/tests/{test.Value}/settings", new TestSettingsRequest(99m, 5)),
            (HttpMethod.Post, $"/api/v1/tests/{test.Value}/questions", new QuestionWriteRequest("Injected question", QuestionType.SingleChoice, 1m, 2)),
            (HttpMethod.Put, $"/api/v1/tests/{test.Value}/questions/{questionId.Value}", new QuestionUpdateRequest("Hijacked question", QuestionType.MultipleChoice, 5m)),
            (HttpMethod.Delete, $"/api/v1/tests/{test.Value}/questions/{questionId.Value}", null),
            (HttpMethod.Patch, $"/api/v1/tests/{test.Value}/questions/{questionId.Value}/order", new OrderRequest(1)),
            (HttpMethod.Post, $"/api/v1/tests/{test.Value}/questions/{questionId.Value}/options", new AnswerOptionWriteRequest("Injected option", false, 2)),
            (HttpMethod.Put, $"/api/v1/tests/{test.Value}/questions/{questionId.Value}/options/{optionId.Value}", new AnswerOptionUpdateRequest("Hijacked option", false)),
            (HttpMethod.Delete, $"/api/v1/tests/{test.Value}/questions/{questionId.Value}/options/{optionId.Value}", null),
            (HttpMethod.Patch, $"/api/v1/tests/{test.Value}/questions/{questionId.Value}/options/{optionId.Value}/order", new OrderRequest(1)),
            (HttpMethod.Post, $"/api/v1/tests/{test.Value}/publish", new PublishRequest(Guid.NewGuid())),
            (HttpMethod.Post, $"/api/v1/tests/{test.Value}/archive", null),
        };

        ApiTestHost.Authenticate(client, AuthorA, AuthorRole);
        foreach (var (method, uri, body) in mutations)
        {
            using var request = new HttpRequestMessage(method, uri);
            if (body is not null)
                request.Content = JsonContent.Create(body);
            request.Headers.TryAddWithoutValidation("If-Match", etag);

            using var response = await client.SendAsync(request, ct);
            Assert.True(
                response.StatusCode == HttpStatusCode.Forbidden,
                $"{method} {uri} ответил {(int)response.StatusCode}, а не 403 — владение здесь не проверяется.");
        }

        // Отказ обязан произойти до записи. Иначе тест на код ответа проходит, а данные
        // всё равно изменены — и это ровно тот отказ, который никто не заметит.
        ApiTestHost.Authenticate(client, AuthorB, AuthorRole);
        var editor = await client.GetFromJsonAsync<TestEditorView>($"/api/v1/tests/{test.Value}/editor", ct);
        Assert.NotNull(editor);
        Assert.Equal("Author B original title", editor.Title);
        Assert.Equal(TestStatus.Draft, editor.Status);
        var question = Assert.Single(editor.Questions);
        Assert.Equal("Original question", question.Text);
        Assert.Equal(QuestionType.SingleChoice, question.Type);
        var option = Assert.Single(question.Options);
        Assert.Equal("Original option", option.Text);
        Assert.True(option.IsCorrect);

        var revisions = await client.GetFromJsonAsync<PublishedRevisionSummary[]>($"/api/v1/tests/{test.Value}/revisions", ct);
        Assert.Empty(Assert.IsType<PublishedRevisionSummary[]>(revisions));
    }

    /// <summary>
    /// Поиск и подсчёт — отдельная ветка SQL от простого перечисления, и фильтр владельца
    /// обязан применяться до них обоих: иначе чужой тест не попадёт в выдачу, но будет
    /// учтён в <c>TotalCount</c>, то есть его существование окажется наблюдаемым.
    /// </summary>
    [Fact]
    public async Task Search_and_paging_count_only_the_callers_own_tests()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        await using var factory = ApiTestHost.Create(database.ConnectionString);
        using var client = factory.CreateClient();

        ApiTestHost.Authenticate(client, AuthorB, AuthorRole);
        await PostValue<TestId>(client, "/api/v1/tests", new CreateTestRequest("Distinctive quantum syllabus"), ct);

        ApiTestHost.Authenticate(client, AuthorA, AuthorRole);
        var own = await PostValue<TestId>(client, "/api/v1/tests", new CreateTestRequest("Author A syllabus"), ct);

        var byExactForeignTitle = await client.GetFromJsonAsync<PagedResult<TestCatalogItem>>(
            "/api/v1/tests?search=Distinctive%20quantum%20syllabus&page=1&pageSize=20", ct);
        Assert.NotNull(byExactForeignTitle);
        Assert.Empty(byExactForeignTitle.Items);
        Assert.Equal(0, byExactForeignTitle.TotalCount);

        var shared = await client.GetFromJsonAsync<PagedResult<TestCatalogItem>>(
            "/api/v1/tests?search=syllabus&page=1&pageSize=20", ct);
        Assert.NotNull(shared);
        Assert.Equal(1, shared.TotalCount);
        Assert.Equal(own, Assert.Single(shared.Items).Id);
    }

    /// <summary>
    /// Эндпоинты попытки ключуются по студенту. Владелец теста не является её владельцем —
    /// даже для собственного теста, — потому что представление попытки показывает ход
    /// прохождения, а рецензенту предназначено <c>/api/v1/results/{attemptId}</c>.
    /// </summary>
    [Fact]
    public async Task The_test_owner_reads_attempts_through_the_reviewer_route_and_no_other()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        await using var factory = ApiTestHost.Create(database.ConnectionString);
        using var client = factory.CreateClient();

        ApiTestHost.Authenticate(client, AuthorB, AuthorRole);
        var test = await PostValue<TestId>(client, "/api/v1/tests", new CreateTestRequest("Owned by author B"), ct);
        var questionId = await PostWithEtag<QuestionId>(client, test, $"/api/v1/tests/{test.Value}/questions",
            new QuestionWriteRequest("Pick correct", QuestionType.SingleChoice, 1m, 1), ct);
        var correctOptionId = await PostWithEtag<AnswerOptionId>(client, test,
            $"/api/v1/tests/{test.Value}/questions/{questionId.Value}/options",
            new AnswerOptionWriteRequest("Correct", true, 1), ct);
        await PostWithEtag<AnswerOptionId>(client, test,
            $"/api/v1/tests/{test.Value}/questions/{questionId.Value}/options",
            new AnswerOptionWriteRequest("Wrong", false, 2), ct);
        var revisionId = await PostWithEtag<PublishedTestRevisionId>(client, test,
            $"/api/v1/tests/{test.Value}/publish", new PublishRequest(Guid.NewGuid()), ct);

        ApiTestHost.Authenticate(client, "admin-1", "test-admin");
        var assignmentId = await PostValue<TestAssignmentId>(client, "/api/v1/assignments", new AssignRequest(
            revisionId.Value, "student-1", null,
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1), 1, Guid.NewGuid()), ct);

        ApiTestHost.Authenticate(client, "student-1");
        var attemptId = await PostValue<TestAttemptId>(client, $"/api/v1/assignments/{assignmentId.Value}/attempts",
            new StartAttemptRequest(Guid.NewGuid()), ct);
        using (var answer = await client.PutAsJsonAsync($"/api/v1/attempts/{attemptId.Value}/answers/{questionId.Value}",
                   new AnswerQuestionRequest([correctOptionId.Value]), ct))
            Assert.Equal(HttpStatusCode.OK, answer.StatusCode);

        foreach (var caller in new[] { AuthorB, AuthorA })
        {
            ApiTestHost.Authenticate(client, caller, AuthorRole);
            foreach (var uri in new[]
                     {
                         $"/api/v1/attempts/{attemptId.Value}",
                         $"/api/v1/attempts/{attemptId.Value}/presentation",
                         $"/api/v1/attempts/{attemptId.Value}/result",
                         $"/api/v1/assignments/{assignmentId.Value}/attempts/active",
                     })
            {
                using var response = await client.GetAsync(uri, ct);
                Assert.True(
                    response.StatusCode == HttpStatusCode.NotFound,
                    $"{caller} получил {(int)response.StatusCode} от {uri}, а ожидается 404: эти представления принадлежат студенту.");
            }
        }

        // Законный путь владельца: рецензентское представление своей же попытки.
        ApiTestHost.Authenticate(client, AuthorB, AuthorRole);
        using (var reviewer = await client.GetAsync($"/api/v1/results/{attemptId.Value}", ct))
            Assert.Equal(HttpStatusCode.OK, reviewer.StatusCode);

        ApiTestHost.Authenticate(client, AuthorA, AuthorRole);
        using (var foreignReviewer = await client.GetAsync($"/api/v1/results/{attemptId.Value}", ct))
            Assert.Equal(HttpStatusCode.NotFound, foreignReviewer.StatusCode);
    }

    /// <summary>
    /// Владелец не может появиться иначе, чем из `sub` вызывающего.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Документация описывала «безопасное заполнение легаси-записей значением
    /// <c>__legacy_admin_only__</c>», а issue #21 пункт 3 требовал проверить на контуре, что
    /// такой владелец не всплывает у произвольного автора. Проверять оказалось нечего: этот
    /// backfill жил в миграциях, которых в репозитории больше нет — история схлопнута в одну
    /// baseline-миграцию, где <c>OwnerId</c> объявлен обязательной колонкой без значения по
    /// умолчанию. Строки с таким владельцем не может создать ни код, ни база.
    /// </para>
    /// <para>
    /// Утверждение из документа сильнее ручной проверки на контуре и не зависит от того, что
    /// на контуре успели создать, — но оно верно ровно до тех пор, пока у колонки нет
    /// значения по умолчанию, а строка «легаси-владельца» не вернётся в код. Тест держит
    /// оба условия.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_owner_can_exist_that_the_application_never_issued()
    {
        var root = RepositoryRoot();

        var migration = File.ReadAllLines(
            Path.Combine(root, "src/TestApp.Infrastructure/Persistence/Migrations/20260813183117_PostgreSqlBaseline.cs"));
        var ownerColumn = Assert.Single(migration, line => line.Contains("OwnerId = table.Column<string>", StringComparison.Ordinal));

        Assert.Contains("nullable: false", ownerColumn, StringComparison.Ordinal);
        Assert.DoesNotContain("defaultValue", ownerColumn, StringComparison.Ordinal);

        // Значение-часовой не должно вернуться ни в код, ни в миграции: единственный способ
        // получить владельца — предъявить токен.
        var sources = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("legacy_admin_only", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(root, file))
            .ToArray();

        Assert.Empty(sources);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TestApp.slnx")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new Xunit.Sdk.XunitException("Не найден корень репозитория (TestApp.slnx).");
    }

    private static async Task<T> PostValue<T>(HttpClient client, string uri, object body, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync(uri, body, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.IsType<T>(await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct));
    }

    private static async Task<T> PostWithEtag<T>(HttpClient client, TestId testId, string uri, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Match", await EtagAsync(client, testId, ct));
        using var response = await client.SendAsync(request, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.IsType<T>(await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct));
    }

    private static async Task<string> EtagAsync(HttpClient client, TestId testId, CancellationToken ct)
    {
        using var response = await client.GetAsync($"/api/v1/tests/{testId.Value}/editor", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response.Headers.ETag?.ToString()
            ?? throw new Xunit.Sdk.XunitException("Редактор не вернул ETag.");
    }
}
