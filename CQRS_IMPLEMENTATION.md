# CQRS Implementation in TestApp

Этот документ описывает реализацию паттерна CQRS (Command Query Responsibility Segregation) в проекте TestApp.

## 📚 Структура проекта

### TestApp.Core/Messages
Базовые интерфейсы и реализация медиатора:
- `ICommand` - маркерный интерфейс для команд
- `IQuery<TResponse>` - интерфейс для запросов с типизированным ответом
- `INotification` - интерфейс для уведомлений
- `ICommandHandler<TCommand, TResponse>` - обработчик команд
- `IQueryHandler<TQuery, TResponse>` - обработчик запросов
- `INotificationHandler<TNotification>` - обработчик уведомлений
- `IMediator` - интерфейс медиатора
- `Mediator` - базовая реализация медиатора

### TestApp.Domain/Commands
Команды для изменения состояния:
- `CreateTestCommand` - создание нового теста
- `UpdateTestCommand` - обновление существующего теста
- `DeleteTestCommand` - удаление теста
- `AddQuestionToTestCommand` - добавление вопроса к тесту
- `AddAnswerToQuestionCommand` - добавление ответа к вопросу
- `TestCommandResult` - результат выполнения команды

### TestApp.Domain/Queries
Запросы для чтения данных:
- `GetTestByIdQuery` - получение теста по ID
- `GetAllTestsQuery` - получение всех тестов
- `GetTestWithDetailsQuery` - получение теста со всеми вопросами и ответами
- `GetQuestionsByTestIdQuery` - получение всех вопросов теста
- `GetAnswersByQuestionIdQuery` - получение всех ответов вопроса
- `SearchTestsByTitleQuery` - поиск тестов по названию

### TestApp.Domain/Handlers
Обработчики команд и запросов:
- `CreateTestCommandHandler` - обработчик создания теста
- `GetAllTestsQueryHandler` - обработчик получения всех тестов

## 🚀 Использование

### Регистрация в DI контейнере

```fsharp
open Microsoft.Extensions.DependencyInjection
open TestApp.Core.Messages
open TestApp.Domain.Handlers

// Регистрация медиатора
services.AddSingleton<IMediator, Mediator>() |> ignore

// Регистрация обработчиков команд
services.AddScoped<ICommandHandler<CreateTestCommand, TestCommandResult>, CreateTestCommandHandler>() |> ignore

// Регистрация обработчиков запросов
services.AddScoped<IQueryHandler<GetAllTestsQuery, seq<TestEntity>>, GetAllTestsQueryHandler>() |> ignore
```

### Отправка команды

```fsharp
open System
open TestApp.Core.Messages
open TestApp.Domain.Commands

let createTest (mediator: IMediator) = task {
    let command = {
        TestTitle = "Тест по математике"
        TestTime = TimeOnly(0, 30, 0) // 30 минут
    }
    
    let! result = mediator.Send<CreateTestCommand, TestCommandResult>(command, None)
    
    if result.Success then
        printfn $"Тест создан с ID: {result.EntityId.Value}"
    else
        printfn $"Ошибка: {result.Message}"
}
```

### Выполнение запроса

```fsharp
open TestApp.Domain.Queries

let getAllTests (mediator: IMediator) = task {
    let query = GetAllTestsQuery()
    
    let! tests = mediator.Query<GetAllTestsQuery, seq<TestEntity>>(query, None)
    
    tests |> Seq.iter (fun test -> 
        printfn $"Тест: {test.TestTitle}, Время: {test.TestTime}")
}
```

## 📝 Преимущества CQRS

1. **Разделение ответственности** - команды изменяют данные, запросы читают
2. **Масштабируемость** - можно отдельно масштабировать чтение и запись
3. **Оптимизация** - разные модели для чтения и записи
4. **Безопасность** - четкое разделение операций
5. **Тестируемость** - легко тестировать изолированные обработчики

## 🔧 Следующие шаги

1. Добавить репозитории для работы с БД
2. Реализовать остальные обработчики команд:
   - UpdateTestCommandHandler
   - DeleteTestCommandHandler
   - AddQuestionToTestCommandHandler
   - AddAnswerToQuestionCommandHandler

3. Реализовать остальные обработчики запросов:
   - GetTestByIdQueryHandler
   - GetTestWithDetailsQueryHandler
   - GetQuestionsByTestIdQueryHandler
   - GetAnswersByQuestionIdQueryHandler
   - SearchTestsByTitleQueryHandler

4. Добавить поведения (behaviors) для:
   - Валидации команд
   - Логирования
   - Обработки ошибок
   - Транзакций

5. Добавить доменные события и их обработчики
6. Интегрировать с Entity Framework Core или другой ORM
7. Добавить unit-тесты для обработчиков

## 📚 Дополнительные ресурсы

- [CQRS Pattern](https://martinfowler.com/bliki/CQRS.html)
- [MediatR Library](https://github.com/jbogard/MediatR) - популярная библиотека для .NET
- [Domain-Driven Design](https://www.domainlanguage.com/ddd/)
