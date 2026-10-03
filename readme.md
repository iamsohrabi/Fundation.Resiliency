# Fundation.Resiliency

کتابخانه‌ای برای استفاده از الگوهای تاب‌آوری در برنامه‌های ‎.NET‎. این کتابخانه
قابلیت‌های زیر را فراهم می‌کند:

- **Retry برای HTTP:** تکرار درخواست‌های ناموفق و موقتی.
- **Circuit Breaker برای HTTP:** توقف موقت درخواست‌ها پس از بروز مکرر خطا.
- **Timeout برای HTTP:** محدود کردن زمان اجرای درخواست.
- **Retry برای MediatR:** اجرای دوبارهٔ درخواست‌های انتخاب‌شده در صورت exception.
- **Circuit Breaker برای MediatR:** توقف موقت اجرای درخواست‌های MediatR پس از
  شکست‌های متوالی.
- **Fallback برای MediatR:** اجرای handler جایگزین در صورت شکست handler اصلی.

سیاست‌ها بر پایهٔ Polly ساخته شده‌اند. ثبت خودکار انواع MediatR نیز با Scrutor
انجام می‌شود.

## سازگاری و نصب

این نسخه برای `net10.0` ساخته می‌شود. پس از انتشار پکیج در NuGet یا فید سازمانی،
پکیج را به پروژه اضافه کنید:

```xml
<PackageReference Include="Fundation.Resiliency" Version="1.0.0" />
```

یا از خط فرمان:

```powershell
dotnet add package Fundation.Resiliency --version 1.0.0
```

## پیکربندی برنامه

سیاست‌های HTTP تنظیمات خود را از بخش `PolicyOptions` دریافت می‌کنند:

```json
{
  "PolicyOptions": {
    "RetryCount": 3,
    "BreakDuration": 30,
    "TimeOutDuration": 30
  }
}
```

| تنظیم | واحد | توضیح |
|---|---:|---|
| `RetryCount` | تعداد تلاش | تعداد تلاش‌های مجدد HTTP؛ تلاش اولیه در این عدد حساب نمی‌شود. پیش‌فرض: `3`. |
| `BreakDuration` | ثانیه | مدت بازماندن Circuit Breaker در HTTP. پیش‌فرض: `30`. |
| `TimeOutDuration` | ثانیه | حداکثر مدت اجرای درخواست HTTP. باید بیشتر از صفر باشد. پیش‌فرض: `30`. |

نمونهٔ راه‌اندازی در برنامهٔ ASP.NET Core:

```csharp
var builder = WebApplication.CreateBuilder(args);

// builder.Configuration به‌طور پیش‌فرض appsettings.json را می‌خواند.
// IConfiguration و ILoggerFactory موردنیاز کتابخانه نیز توسط میزبان ثبت می‌شوند.

var app = builder.Build();
app.Run();
```

## HTTP Client

### تعریف typed client

```csharp
public interface IInventoryClient
{
    Task<HttpResponseMessage> GetItemAsync(
        int id,
        CancellationToken cancellationToken = default);
}

public sealed class InventoryClient(HttpClient httpClient) : IInventoryClient
{
    public Task<HttpResponseMessage> GetItemAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        return httpClient.GetAsync($"items/{id}", cancellationToken);
    }
}
```

### ثبت با `AddHttpApiClient`

`AddHttpApiClient` typed client را ثبت می‌کند و به آن Timeout، Retry و Circuit
Breaker می‌افزاید. نام HttpClient در این متد `nameof(TClient)` است؛ برای تنظیم
`BaseAddress` روی همان named client، آن را با همان نام پیکربندی کنید:

```csharp
using Fundation.Resiliency.Extensions;

builder.Services.AddHttpClient(
    nameof(InventoryClient),
    client => client.BaseAddress = new Uri("https://inventory.example.com/"));

builder.Services.AddHttpApiClient<IInventoryClient, InventoryClient>();
```

سپس client را از DI دریافت و استفاده کنید:

```csharp
public sealed class InventoryService(IInventoryClient inventoryClient)
{
    public async Task<string> GetItemAsync(
        int id,
        CancellationToken cancellationToken)
    {
        using var response = await inventoryClient.GetItemAsync(id, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}
```

### ثبت مستقیم و افزودن handlerهای استاندارد

اگر می‌خواهید خودتان typed client را پیکربندی کنید یا callback متد
`AddCustomPolicyHandlers` را به‌کار ببرید، `HttpClient` را مستقیم ثبت کنید:

```csharp
using Fundation.Resiliency.Extensions;

builder.Services
    .AddHttpClient<IInventoryClient, InventoryClient>(
        nameof(InventoryClient),
        client => client.BaseAddress = new Uri("https://inventory.example.com/"))
    .AddCustomPolicyHandlers();
```

callback اختیاری، پس از اضافه‌شدن handlerهای تاب‌آوری اجرا می‌شود:

```csharp
builder.Services
    .AddHttpClient<IInventoryClient, InventoryClient>(
        nameof(InventoryClient),
        client => client.BaseAddress = new Uri("https://inventory.example.com/"))
    .AddCustomPolicyHandlers(httpClientBuilder =>
    {
        // نمونه: تنظیمات یا handlerهای اختصاصی این client
        return httpClientBuilder;
    });
```

`AddHttpApiClient` خودش handlerهای استاندارد را اضافه می‌کند. آن را با
`AddCustomPolicyHandlers` روی همان client ترکیب نکنید؛ در غیر این صورت pipelineهای
تاب‌آوری دوباره اضافه می‌شوند.

### افزودن جداگانهٔ سیاست‌های HTTP

در صورت نیاز می‌توانید فقط handlerهای موردنظر را اضافه کنید:

```csharp
builder.Services
    .AddHttpClient<IInventoryClient, InventoryClient>(
        nameof(InventoryClient),
        client => client.BaseAddress = new Uri("https://inventory.example.com/"))
    .AddTimeoutHandler()
    .AddRetryPolicyHandler()
    .AddCircuitBreakerHandler();
```

این handlerها به `IConfiguration` برای خواندن `PolicyOptions` و به
`ILoggerFactory` برای ثبت رخدادها نیاز دارند. میزبان‌های ASP.NET Core این سرویس‌ها
را به‌طور معمول فراهم می‌کنند.

### HTTP Retry

Retry برای این موارد انجام می‌شود:

- exception از نوع `HttpRequestException`
- پاسخ HTTP با وضعیت `408 Request Timeout`
- پاسخ HTTP با status codeهای `500` تا `599`

کدهای دیگر، از جمله سایر پاسخ‌های `4xx`، Retry نمی‌شوند. تعداد تلاش‌های مجدد از
`PolicyOptions:RetryCount` گرفته می‌شود. فاصلهٔ تلاش‌ها نمایی است:

```text
تأخیر تلاش n = 2^n ثانیه + مقداری تصادفی بین 0 تا 99 میلی‌ثانیه
```

هر Retry با `ILogger` ثبت می‌شود. مقدار Retry را با توجه به idempotent بودن عملیات
تنظیم کنید؛ تکرار خودکار درخواست‌های تغییردهندهٔ وضعیت ممکن است اثر عملیات را چندبار
اعمال کند.

### HTTP Circuit Breaker

Circuit Breaker همان خطاهای HTTP مربوط به Retry را بررسی می‌کند. نسبت خطای لازم برای
بازشدن مدار `100%` است. حداقل تعداد درخواست‌ها برای ارزیابی برابر است با بزرگ‌ترِ
`2` و `RetryCount + 1`. مدت بازبودن مدار از `BreakDuration`، برحسب ثانیه، خوانده
می‌شود.

پس از بازشدن مدار، درخواست‌ها برای مدت تعیین‌شده رد می‌شوند؛ پس از پایان این مدت،
Polly اجازهٔ ارزیابی مجدد سرویس را می‌دهد. باز و بسته‌شدن مدار در log ثبت می‌شود.

### HTTP Timeout

`AddHttpApiClient` و `AddCustomPolicyHandlers` هر دو Timeout را نیز اضافه می‌کنند.
مدت آن از `TimeOutDuration` برحسب ثانیه خوانده می‌شود و مقدار نامعتبرِ صفر یا کمتر
هنگام ساخت pipeline با خطای پیکربندی مشخص می‌شود. پایان timeout باعث لغو درخواست
می‌شود؛ کد فراخواننده باید لغو را مدیریت کند.

## MediatR

متدهای ثبت MediatR در این کتابخانه به‌صورت static روی کلاس `Extensions` تعریف شده‌اند.
در مثال‌های زیر فرض شده است MediatR و handlerهای اصلی برنامه نیز ثبت می‌شوند.

### تعریف request و response نمونه

```csharp
using Fundation.Resiliency.Retry;
using MediatR;

public sealed record InventoryResult(int ItemId, bool IsAvailable)
{
    public static InventoryResult Unavailable(int itemId) =>
        new(itemId, false);
}

public sealed record GetInventoryQuery(int ItemId)
    : IRequest<InventoryResult>,
      IRetryableRequest<GetInventoryQuery, InventoryResult>
{
    public int RetryAttempts => 3;
    public int RetryDelay => 250;
    public bool RetryWithExponentialBackoff => true;
    public int ExceptionsAllowedBeforeCircuitTrip => 1;
    public int CircuitBreakDuration => 30;
}
```

### ثبت MediatR، Retry و Fallback

`AddMediaterRetryPolicy` و `AddMediaterFallbackPolicy` علاوه بر ثبت pipeline
behaviorها، assemblyهای داده‌شده را برای request policyها و fallback handlerها
اسکن می‌کنند:

```csharp
using Fundation.Resiliency.Extensions;
using MediatR;

var applicationAssembly = typeof(GetInventoryQuery).Assembly;

builder.Services.AddMediatR(applicationAssembly);

Extensions.AddMediaterRetryPolicy(
    builder.Services,
    new[] { applicationAssembly });

Extensions.AddMediaterFallbackPolicy(
    builder.Services,
    new[] { applicationAssembly });
```

نام متدهای عمومی در نسخهٔ فعلی دقیقاً `AddMediaterRetryPolicy` و
`AddMediaterFallbackPolicy` است.

### MediatR Retry با `IRetryableRequest`

درخواست‌هایی که `IRetryableRequest<TRequest, TResponse>` را پیاده‌سازی می‌کنند، از
سیاست Retry استفاده خواهند کرد:

| عضو | کاربرد | پیش‌فرض |
|---|---|---:|
| `RetryAttempts` | تعداد تلاش‌های مجدد، جدا از تلاش اولیه | `1` |
| `RetryDelay` | تأخیر پایه برحسب میلی‌ثانیه | `250` |
| `RetryWithExponentialBackoff` | استفاده از تأخیر نمایی به‌جای ثابت | `false` |
| `ExceptionsAllowedBeforeCircuitTrip` | تعداد شکست‌های قابل‌تحمل پیش از آستانهٔ مدار | `1` |
| `CircuitBreakDuration` | مدت بازبودن مدار، برحسب ثانیه | `30` |

برای `RetryWithExponentialBackoff = true`، تأخیر هر تلاش از رابطهٔ
`2^شماره‌تلاش * RetryDelay` محاسبه می‌شود. در حالت `false`، تأخیر ثابت است.

Retry در MediatR همهٔ exceptionها به‌جز `OperationCanceledException` را قابل‌تکرار
می‌داند. بنابراین برای جلوگیری از تکرار خطاهای برنامه‌نویسی یا عملیات غیر idempotent،
این قابلیت را فقط برای requestهای مناسب فعال کنید.

Circuit Breaker MediatR بر اساس خطای نهایی هر اجرای request عمل می‌کند؛ خطایی که پس
از تلاش‌های Retry باقی بماند یک شکست محسوب می‌شود. آستانهٔ واقعی پنجره برابر است با
`ExceptionsAllowedBeforeCircuitTrip + 1` و مدار زمانی باز می‌شود که نمونه‌های لازم
در پنجرهٔ ارزیابی شکست خورده باشند. وضعیت pipeline میان درخواست‌های هم‌نوع با
تنظیمات یکسان حفظ می‌شود.

### MediatR Retry با `RetryPolicyAttribute`

اگر نخواهید interface را روی request پیاده‌سازی کنید، می‌توانید attribute را روی
کلاس request قرار دهید:

```csharp
using Fundation.Resiliency.Retry;
using MediatR;

[RetryPolicy(
    RetryCount = 3,
    SleepDuration = 200,
    ExceptionsAllowedBeforeCircuitTrip = 1,
    CircuitBreakDuration = 30)]
public sealed record FindInventoryQuery(int ItemId)
    : IRequest<InventoryResult>;
```

در این روش:

- `RetryCount` تعداد تلاش‌های مجدد است.
- `SleepDuration` زمان پایهٔ تأخیر برحسب میلی‌ثانیه است.
- تأخیرها افزایشی‌اند؛ مثلاً با مقدار `200`، تأخیرهای ۲۰۰، ۴۰۰ و ۶۰۰ میلی‌ثانیه
  خواهند بود.
- `ExceptionsAllowedBeforeCircuitTrip` و `CircuitBreakDuration` تنظیمات مدار را
  تعیین می‌کنند.

اگر request هم attribute داشته باشد و هم `IRetryableRequest<TRequest, TResponse`
را پیاده‌سازی کند، تنظیمات interface اولویت دارد.

### MediatR Fallback

برای requestای که نیاز به پاسخ جایگزین دارد، یک `IFallbackHandler<TRequest,
TResponse>` تعریف کنید. باید فقط یک fallback handler برای هر نوع request ثبت شود:

```csharp
using Fundation.Resiliency.Fallback;

public sealed class GetInventoryFallbackHandler
    : IFallbackHandler<GetInventoryQuery, InventoryResult>
{
    public Task<InventoryResult> HandleFallbackAsync(
        GetInventoryQuery request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(
            InventoryResult.Unavailable(request.ItemId));
    }
}
```

وقتی اجرای handler اصلی با exception ناموفق شود، `HandleFallbackAsync` برای تولید
پاسخ جایگزین اجرا می‌شود. `OperationCanceledException` به‌عنوان شکست fallback
محاسبه نمی‌شود. اگر خود fallback exception بدهد، exception آن به فراخواننده منتقل
می‌شود.

### MediatR Timeout

در این نسخه MediatR timeout پیاده‌سازی نشده است. MediatR 11 در این پروژه
`RequestHandlerDelegate` بدون پارامتر cancellation token ارائه می‌کند؛ در نتیجه
timeoutای که فقط انتظار را متوقف کند تضمین نمی‌کند اجرای handler واقعاً لغو شود.
Timeout موجود صرفاً برای `HttpClient` است.

## نکات اجرایی

- در هر نوع request حداکثر یک retry policy و یک fallback handler ثبت کنید. ثبت چند
  مورد باعث `InvalidOperationException` صریح هنگام اجرای همان request می‌شود.
- Retry برای همهٔ exceptionها به‌جز لغو عملیات فعال است؛ نوع خطاها را در handlerها
  کنترل کنید و فقط عملیات قابل تکرار را Retry کنید.
- `AddHttpApiClient` خودش handlerهای Timeout، Retry و Circuit Breaker را ثبت می‌کند؛
  از ثبت مجدد `AddCustomPolicyHandlers` روی همان client خودداری کنید.
- تنظیمات Circuit Breaker و Timeout را از configuration مناسب هر محیط (توسعه،
  staging و production) تأمین کنید.

## ساختار پروژه

```text
Fundation.Resiliency/
├── CircuitBreaker/
│   ├── HttpCircuitBreakerPolicies.cs
│   └── ICircuitBreakerPolicyOptions.cs
├── Extensions/
│   ├── HttpClientBuilderExtensions.cs
│   ├── HttpClientBuilderExtensions.Retry.cs
│   ├── HttpClientBuilderExtensions.CircuitBreaker.cs
│   ├── HttpClientBuilderExtensions.Timeout.cs
│   └── ServiceCollectionExtensions.cs
├── Fallback/
│   ├── FallbackBehavior.cs
│   └── IFallbackHandler.cs
├── Retry/
│   ├── HttpPolicyBuilders.cs
│   ├── HttpRetryPolicies.cs
│   ├── IRetryPolicyOptions.cs
│   ├── IRetryableRequest.cs
│   ├── RetryBehavior.cs
│   └── RetryPolicyAttribute.cs
├── Timeout/
│   └── ITimeoutPolicyOptions.cs
└── PolicyOptions.cs
```

## وابستگی‌های اصلی

- `Polly` و `Microsoft.Extensions.Http.Resilience` برای pipelineهای تاب‌آوری.
- `MediatR` برای pipeline behaviorهای درخواست.
- `Scrutor` برای اسکن و ثبت خودکار policyها و fallback handlerها.
- `Microsoft.Extensions.Http` برای `IHttpClientFactory` و typed clientها.
