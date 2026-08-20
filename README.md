# HealthChecks demo: два ASP.NET Core сервиса

Учебный проект на .NET 10:

- Service A проверяет одну PostgreSQL-базу;
- Service B проверяет одну PostgreSQL-базу и доступность Service A;
- каждый сервис проверяет собственный Redis cache;
- все проверки находятся в едином массиве `Dependencies`;
- новый элемент поддерживаемого типа регистрируется через общий `foreach`;
- параметры находятся в отдельных `healthCheck.json`;
- `appsettings.json` для health checks не используется;
- добавлены Docker Compose и пример Docker Swarm stack.

## Главный ориентир

Реализация в первую очередь основана на официальной документации
[Health checks in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0):

- собственные проверки реализуют `IHealthCheck`;
- результат возвращается через `HealthCheckResult`;
- проверки регистрируются через `AddHealthChecks().AddCheck<T>()`;
- readiness фильтруется по тегу `readiness`;
- liveness использует `Predicate = _ => false`;
- detailed JSON формируется из стандартного `HealthReport`;
- используются нативные статусы `Healthy`, `Degraded`, `Unhealthy`;
- используются стандартные HTTP-коды middleware.

Стандартное соответствие:

| HealthStatus | HTTP status |
| --- | --- |
| `Healthy` | 200 |
| `Degraded` | 200 |
| `Unhealthy` | 503 |

## Архитектура

| Сервис | Проверки |
| --- | --- |
| Service A | `database`, `redis-cache` |
| Service B | `database`, `redis-cache`, `service-a` |

Endpoints:

| Endpoint | Формат | Назначение |
| --- | --- | --- |
| `/health/live` | Стандартный plaintext | Проверяет только процесс и HTTP pipeline. |
| `/health/ready` | Стандартный plaintext | Запускает проверки с тегом `readiness`. |
| `/health` | JSON | Запускает все проверки и возвращает подробный `HealthReport`. |

Service B вызывает `http://service-a:8080/health/ready`. Readiness Service A
проверяет его PostgreSQL и Redis, поэтому отказ любой из этих критичных
зависимостей делает Service A, а затем и Service B, неготовыми.

## Единый массив проверок

Все зависимости находятся в одном массиве. `Type` выбирает готовый способ
регистрации, поэтому новый элемент уже поддерживаемого типа требует только
изменения JSON и перезапуска приложения:

```json
"Dependencies": [
  {
    "Type": "PostgreSql",
    "Name": "database",
    "ComponentId": "service-a-db-1",
    "ComponentType": "datastore",
    "FailureStatus": "Unhealthy",
    "TimeoutSeconds": 3,
    "Tags": [ "readiness", "database", "critical" ]
  },
  {
    "Type": "Redis",
    "Name": "redis-cache",
    "ComponentId": "service-a-redis",
    "ComponentType": "datastore",
    "FailureStatus": "Unhealthy",
    "TimeoutSeconds": 3,
    "Tags": [ "readiness", "cache", "critical" ]
  }
]
```

`DependencyHealthCheckSettings` содержит только общие поля и nullable-блок
`Http`. Внутри `HttpHealthCheckSettings` свойство `Url` не nullable. У
PostgreSQL и Redis блок `Http` отсутствует. Connection strings вынесены в
отдельную секцию `ExternalResources`.

Конфигурация снова читается стандартным `ConfigurationBinder`:

```csharp
var healthSettings = builder.Configuration
    .GetRequiredSection(HealthCheckSettings.SectionName)
    .Get<HealthCheckSettings>()
    ?? throw new InvalidOperationException("Секция HealthChecks не найдена.");

healthSettings.Validate();
```

В `Program.cs` остается только вызов отдельного extension-метода:

```csharp
builder.Services.AddConfiguredHealthChecks(healthSettings.Dependencies);
```

`NpgsqlDataSource` регистрируется отдельно в `Program.cs`, после чего
`AddConfiguredHealthChecks` только добавляет проверку, использующую готовую
зависимость из DI. Сам extension-метод находится в отдельном файле
`HealthCheckServiceCollectionExtensions.cs`. Общего проекта `Common` нет:
каждый сервис владеет собственной регистрацией. Один `foreach` сопоставляет
`PostgreSql`, `Redis` и `Http` с готовой реализацией проверки.

## Detailed JSON

Microsoft не задает обязательный сложный JSON-формат и предлагает формировать
его через `HealthCheckOptions.ResponseWriter`. Проект возвращает удобное
представление стандартного `HealthReport`:

```json
{
  "status": "Degraded",
  "service": {
    "id": "service-b",
    "description": "Health of Service B",
    "version": "1.0",
    "releaseId": "local"
  },
  "totalDurationMs": 2004.73,
  "checks": {
    "database": {
      "status": "Healthy",
      "description": "PostgreSQL доступен и успешно выполняет запросы.",
      "durationMs": 4.11,
      "tags": [
        "critical",
        "database",
        "readiness"
      ],
      "data": {
        "componentId": "service-b-db-1",
        "componentType": "datastore"
      }
    },
    "redis-cache": {
      "status": "Healthy",
      "description": null,
      "durationMs": 2.31,
      "tags": [
        "cache",
        "readiness"
      ],
      "data": {
        "componentId": "service-b-redis",
        "componentType": "datastore"
      }
    },
    "service-a": {
      "status": "Degraded",
      "description": "Service A недоступен по HTTP.",
      "durationMs": 2000.62,
      "tags": [
        "http",
        "optional",
        "readiness"
      ],
      "data": {
        "componentId": "service-a",
        "componentType": "component"
      }
    }
  }
}
```

Никакого преобразования статусов нет:

```csharp
status = report.Status.ToString();
```

Исключения, пароли и connection strings в JSON не выводятся.

## Что взято из RFC draft

[draft-inadarei-api-health-check-06](https://datatracker.ietf.org/doc/html/draft-inadarei-api-health-check-06)
используется только как дополнительный ориентир:

- detailed endpoint возвращает `application/health+json`;
- у ответа есть информация о сервисе;
- у зависимостей есть `componentId` и `componentType`.

Не используются неудобные для этого проекта части draft:

- статусы `pass`, `warn`, `fail`;
- массив для каждой единственной записи в `checks`;
- дублирующие поля `notes` и `output`;
- отдельное поле времени для каждой проверки;
- RFC-специфичные имена вида `component:responseTime`.

Документ является истекшим Internet-Draft, а не опубликованным RFC.

## Пример Degraded для необязательной зависимости

В рабочей конфигурации Service A является критичной зависимостью Service B и
имеет `FailureStatus: Unhealthy`. Это необходимо, чтобы Service B не запускался,
пока Service A и его зависимости не готовы.

Если в другом сервисе интеграция необязательна, ее можно настроить так:

```json
{
  "Type": "Http",
  "Name": "service-a",
  "ComponentId": "service-a",
  "ComponentType": "component",
  "FailureStatus": "Degraded",
  "Tags": [ "readiness", "http", "optional" ],
  "Http": {
    "Url": "http://service-a:8080/health/ready",
    "Method": "GET",
    "ExpectedStatusCodes": [ 200 ]
  }
}
```

Проверка сценария:

```bash
docker compose stop service-a

curl -i http://localhost:8082/health/ready
curl -i http://localhost:8082/health
```

Результат:

- readiness возвращает HTTP 200 и стандартный текст `Degraded`;
- detailed возвращает HTTP 200 и JSON со `status: "Degraded"`;
- проверка `service-a` также имеет статус `Degraded`;
- проверка БД остается `Healthy`.

Такую настройку нельзя использовать как условие строгого запуска: стандартный
health-check middleware возвращает HTTP 200 для `Degraded`.

## Проверка PostgreSQL

`PostgreSqlHealthCheck` открывает соединение через `NpgsqlDataSource` и выполняет
`SELECT 1`. Так проверяется не только сетевое подключение, но и способность
PostgreSQL выполнить простой запрос.

`NpgsqlDataSource` зарегистрирован как singleton, поэтому connection pool
переиспользуется.

## Проверка Redis

Для Redis используется пакет `AspNetCore.HealthChecks.Redis` и готовый метод:

```csharp
.AddRedis(
    connectionStringFactory: serviceProvider =>
        serviceProvider
            .GetRequiredService<ExternalResourceSettings>()
            .Redis
            .ConnectionString,
    name: dependency.Name,
    failureStatus: dependency.GetFailureStatus(),
    tags: dependency.Tags,
    timeout: dependency.GetTimeout())
```

Настройки Service A:

```json
{
  "Type": "Redis",
  "Name": "redis-cache",
  "ComponentId": "service-a-redis",
  "ComponentType": "datastore",
  "FailureStatus": "Unhealthy",
  "TimeoutSeconds": 3,
  "Tags": [ "readiness", "cache", "critical" ]
}
```

В Docker нельзя указывать `localhost:6379`: внутри контейнера это адрес самого
API-контейнера. Используются DNS-имена Compose-сервисов `service-a-redis` и
`service-b-redis`. При локальном запуске API без Docker connection string можно
переопределить на `localhost:6379`.

Пакет поддерживается сообществом Xabaril и не является компонентом,
поддерживаемым Microsoft.

## Отдельный healthCheck.json

```csharp
builder.Configuration
    .AddJsonFile("healthCheck.json", optional: false, reloadOnChange: false)
    .AddEnvironmentVariables();
```

Переменные окружения добавляются после JSON и имеют больший приоритет:

```text
ExternalResources__PostgreSql__ConnectionString=...
ExternalResources__Redis__ConnectionString=service-a-redis:6379
HealthChecks__Dependencies__2__Http__Url=http://service-a:8080/health/ready
```

Индекс соответствует позиции зависимости в массиве. Для секретов в production
лучше использовать отдельный secret provider.

Изменение health-check конфигурации требует перезапуска приложения, поскольку
регистрации и `NpgsqlDataSource` создаются при старте.

## Запуск

```bash
docker compose up --build --detach
docker compose ps
```

Проверка endpoints:

```bash
curl -i http://localhost:8081/health/live
curl -i http://localhost:8081/health/ready
curl -i http://localhost:8081/health

curl -i http://localhost:8082/health/live
curl -i http://localhost:8082/health/ready
curl -i http://localhost:8082/health
```

Smoke test:

```bash
chmod +x scripts/smoke-test.sh
./scripts/smoke-test.sh
```

Проверка структуры конфигурации:

```bash
chmod +x scripts/validate-config.sh
./scripts/validate-config.sh
```

## Отказ критичной БД

```bash
docker compose stop service-a-db-1

curl -i http://localhost:8081/health/live
curl -i http://localhost:8081/health/ready
curl -i http://localhost:8081/health
```

Ожидается:

- liveness: HTTP 200 и `Healthy`;
- readiness: HTTP 503 и `Unhealthy`;
- detailed: HTTP 503 и JSON со `status: "Unhealthy"`;
- Docker не перезапускает API, потому что container healthcheck использует liveness.

Восстановление:

```bash
docker compose start service-a-db-1
```

## Отказ Redis

```bash
docker compose stop service-a-redis

curl -i http://localhost:8081/health/ready
curl -i http://localhost:8081/health
```

По умолчанию `AddRedis` использует `FailureStatus = Unhealthy`, поэтому
readiness и detailed вернут HTTP 503. Восстановление:

```bash
docker compose start service-a-redis
```

Остановка:

```bash
docker compose down
```

Удаление вместе с тестовыми данными:

```bash
docker compose down --volumes
```

## Docker Swarm

### Почему `depends_on` недостаточно

`depends_on: condition: service_healthy` работает с `docker compose up`, но
Swarm не предоставляет аналогичного механизма зависимости между сервисами.
При `docker stack deploy` задачи разных сервисов планируются независимо.

Поэтому Service B использует startup gate в собственном контейнере:

1. Swarm создает задачу Service B.
2. `/app/wait-for-service-a.sh` находит в `Dependencies` HTTP-зависимость с
   `StartupWait.Enabled = true` и читает ее URL и интервалы.
3. Скрипт вызывает `http://service-a:8080/health/ready`.
4. Service A внутри readiness проверяет PostgreSQL и Redis.
5. Только после HTTP 200 с телом `Healthy` выполняется
   `exec dotnet ServiceB.dll`.
6. Если за 120 секунд Service A не готов, контейнер завершается с кодом 1;
   `restart_policy` Swarm создает новую задачу, и ожидание повторяется.

Фрагмент конфигурации Service B:

```json
{
  "Type": "Http",
  "Name": "service-a",
  "FailureStatus": "Unhealthy",
  "Tags": [ "readiness", "http", "critical" ],
  "Http": {
    "Url": "http://service-a:8080/health/ready",
    "StartupWait": {
      "Enabled": true,
      "TimeoutSeconds": 120,
      "RetryDelaySeconds": 2,
      "RequestTimeoutSeconds": 5
    }
  }
}
```

Это означает, что контейнер Service B уже создан Swarm, но процесс ASP.NET Core
и порт 8080 еще не запущены. Если требуется, чтобы сама задача Service B вообще
не создавалась до готовности Service A, это нужно делать внешним deployment
script или CI/CD: сначала развернуть Service A, дождаться readiness, затем
масштабировать Service B.

Docker healthcheck самого Service B продолжает вызывать `/health/live`. После
запуска приложения его `/health/ready` отдельно проверяет Service A, поэтому
последующий отказ Service A переводит Service B в `Unhealthy`, но не создает
бесполезный цикл перезапусков живого процесса.

### Сборка и развертывание

Swarm не собирает images. Сначала соберите и отправьте их в registry:

```bash
docker build -f src/ServiceA/Dockerfile -t registry.example.com/health-demo/service-a:1.0 .
docker build -f src/ServiceB/Dockerfile -t registry.example.com/health-demo/service-b:1.0 .
docker push registry.example.com/health-demo/service-a:1.0
docker push registry.example.com/health-demo/service-b:1.0
```

Замените `image` в `stack.yaml`, затем выполните:

```bash
docker stack deploy --compose-file stack.yaml health-demo
docker stack services health-demo
docker service logs --follow health-demo_service-b
```

Пока Service A не готов, в логах Service B будет:

```text
Ожидание Healthy от Service A: http://service-a:8080/health/ready
Service A пока не готов; повтор через 2 с.
```

После готовности БД, Redis и Service A:

```text
Service A готов. Запуск Service B.
```

`healthCheck.json` передается через Docker configs и монтируется в
`/app/healthCheck.json`.

Ограничение `node.role == manager` для БД добавлено только для демо. В
production БД лучше размещать вне Swarm либо использовать правильно настроенное
persistent storage и Docker secrets.

## Сборка без Docker

```bash
dotnet restore HealthChecksDemo.slnx
dotnet build HealthChecksDemo.slnx --configuration Release --no-restore
```

GitHub Actions workflow проверяет JSON, Docker Compose и собирает solution.
