# VirtualPathProvider Lab

Лабораторная работа демонстрирует динамическую генерацию виртуальных страниц средствами ASP.NET, IIS и CLR, а также показывает, как обнаружить подобную активность через ETW-трассировку IIS.

## Как запустить

1. Откройте решение в Visual Studio и запустите проект (F5 / IIS Express).
2. На стартовой странице (`Default.aspx`) нажмите **Register VPP (and activate)**.
3. Перейдите по ссылке, отображённой в статусе (`/vpp/googlecheck.aspx?token=labtoken-change-me`).
4. Виртуальная страница проверит доступность `google.com` и вернёт результат в виде текста.
5. Кнопка **Deactivate (AND unregister)** очищает дисковый кэш компиляции (`Temporary ASP.NET Files`), сбрасывает VPP и перезапускает AppDomain через `HttpRuntime.UnloadAppDomain()`.
6. Кнопка **Flush disk compilation cache** удаляет скомпилированные файлы из `HttpRuntime.CodegenDir` без перезапуска AppDomain — полезно для диагностики.

## Продемонстрированные возможности

### IIS / ASP.NET pipeline

| Возможность                         | Где используется                                                                                                                                                                                                                                                                                                       |
| ----------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Default Document**                | `Web.config` → `<defaultDocument>` направляет корневой запрос `/` на `Default.aspx` без явного указания файла.                                                                                                                                                                                                         |
| **Виртуальные пути**                | `LabVirtualPathProvider` перехватывает запрос к `~/googlecheck.aspx`, хотя файла на диске не существует. IIS передаёт обработку ASP.NET, а тот — зарегистрированному `VirtualPathProvider`.                                                                                                                            |
| **Динамическая компиляция страниц** | ASP.NET компилирует `.aspx`-разметку, возвращённую `LabVirtualFile.Open()`, в IL-код «на лету», как если бы файл лежал на диске.                                                                                                                                                                                       |
| **Цепочка провайдеров**             | `LabVirtualPathProvider` делегирует запросы предыдущему провайдеру через свойство `Previous`, сохраняя стандартное поведение для всех остальных путей.                                                                                                                                                                 |
| **`GetCacheDependency`**            | Переопределение возвращает `null` для виртуального пути, предотвращая попытку `FileChangesMonitor` отслеживать несуществующий физический каталог.                                                                                                                                                                      |
| **`MemoryBuildResultCache`**        | In-memory кэш скомпилированных страниц; очищается при рестарте AppDomain.                                                                                                                                                                                                                                              |
| **`DiskBuildResultCache`**          | На полном IIS скомпилированные `.aspx` сохраняются на диск в `Temporary ASP.NET Files` (`HttpRuntime.CodegenDir`). Этот кэш **переживает** рестарт AppDomain и пула приложений — именно поэтому виртуальная страница продолжала работать после деактивации. Для полной очистки необходимо удалить файлы из этой папки. |

### CLR / .NET Framework

| Возможность                                          | Где используется                                                                                                                                |
| ---------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------- |
| **`HostingEnvironment.RegisterVirtualPathProvider`** | Регистрация кастомного провайдера в рантайме по нажатию кнопки — без перезапуска AppDomain. Публичного API для снятия регистрации нет.          |
| **`HttpRuntime.UnloadAppDomain`**                    | Единственный надёжный способ «разрегистрировать» VPP — перезапустить AppDomain, что очищает `MemoryBuildResultCache` и всю цепочку провайдеров. |
| **`HttpRuntime.CodegenDir`**                         | Путь к папке `Temporary ASP.NET Files` для текущего приложения. Используется для программной очистки дискового кэша компиляции.                 |
| **`volatile` поля**                                  | `LabVppState.Registered` и `LabVppState.Active` объявлены `volatile`, что гарантирует видимость изменений между потоками ASP.NET thread pool.   |
| **`VirtualFile` / `VirtualPathProvider`**            | Абстрактные классы из `System.Web.Hosting`, позволяющие подменять файловую систему для ASP.NET.                                                 |
| **`HttpWebRequest`**                                 | Виртуальная страница выполняет синхронный HTTP-запрос к `google.com/generate_204` для проверки внешней сетевой доступности.                     |

### C#

| Возможность                      | Где используется                                                                                                                                           |
| -------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Expression-bodied members**    | `private VirtualPathProvider PrevOrNull => Previous;` — свойство с expression body.                                                                        |
| **Inline ASP.NET разметка**      | Содержимое виртуальной `.aspx`-страницы формируется как verbatim-строка (`@"..."`) и отдаётся через `MemoryStream`.                                        |
| **`sealed` классы**              | `LabVirtualPathProvider` и `LabVirtualFile` помечены `sealed`, предотвращая нежелательное наследование и позволяя JIT-оптимизации девиртуализовать вызовы. |
| **`static readonly` vs `const`** | Токен хранится как `static readonly string`, а не `const`, чтобы его можно было подменить через рефлексию в тестах без перекомпиляции зависимых сборок.    |

## Обнаружение через ETW (Event Tracing for Windows)

Использование `VirtualPathProvider` может быть обнаружено на стороне IIS при помощи провайдера ETW **`Microsoft-Windows-IIS-Configuration`** (`{dc0b8e51-4863-407a-bc3c-1b479b2978ac}`). В проект включены артефакты трассировки, демонстрирующие этот процесс.

### Ключевые ETW-события

| Event ID              | Уровень | Канал    | Описание                                                                         | Индикатор VPP                                                                                 |
| --------------------- | ------- | -------- | -------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------- |
| **25**                | Verbose | Analytic | Виртуальный путь `{ConfigPath}` сопоставлен с физическим путём `{PhysicalPath}`. | Физический путь указывает на файл, **которого нет** на диске (`googlecheck.aspx`).            |
| **47**                | Verbose | Analytic | Папка конфигурации `{ConfigPath}` сопоставлена с каталогом `{Directory}`.        | IIS пытается найти `web.config` рядом с несуществующим файлом.                                |
| **28**                | Info    | Debug    | Олицетворение маркера доступа `{ImpersonationTokenHandle}`.                      | Сопровождает обращение к виртуальному пути — фиксирует контекст безопасности.                 |
| **13**                | Verbose | Analytic | Анализ файла конфигурации `{PhysicalPath}`.                                      | Показывает цепочку конфигурации, по которой IIS дошёл до виртуального пути.                   |
| **17 / 18 / 19 / 21** | Verbose | Debug    | Мониторы файловых изменений: создание, ожидание, уведомление, удаление.          | IIS создаёт `FileChangeNotificationMonitor` для каталогов, включая пути к виртуальным файлам. |
| **9**                 | Verbose | Debug    | Сброс кэша конфигурации для `{ConfigPath}` и вложенных путей.                    | Срабатывает при `HttpRuntime.UnloadAppDomain()` — признак деактивации VPP.                    |

### Пример обнаружения (из `iis_etw_events.ndjson`)

При обращении к виртуальной странице `googlecheck.aspx` процесс `w3wp` (PID 53664) генерирует характерную пару событий:

1. **EventID(25)** — маппинг виртуального пути на физический:
   ```
   ConfigPath: MACHINE/WEBROOT/APPHOST/DEFAULT WEB SITE/VppLab/googlecheck.aspx
   PhysicalPath: C:\inetpub\VppLab\googlecheck.aspx
   ```
2. **EventID(47)** — поиск `web.config` для этого пути:
   ```
   ConfigPath: MACHINE/WEBROOT/APPHOST/DEFAULT WEB SITE/VppLab/googlecheck.aspx
   Directory: \\?\C:\inetpub\VppLab\googlecheck.aspx
   ```

Если `PhysicalPath` из EventID(25) **не соответствует реальному файлу на диске**, это прямой признак работы кастомного `VirtualPathProvider`.

### Как собрать трассировку

```powershell
# Запуск сессии ETW
logman create trace IIS-VPP-Detect -p "Microsoft-Windows-IIS-Configuration" 0xFFFFFFFF 0xFF -o iis_config.etl -ets

# ... воспроизвести обращение к виртуальной странице ...

# Остановка
logman stop IIS-VPP-Detect -ets
```

Для конвертации `.etl` → NDJSON использовался [PerfView](https://github.com/microsoft/perfview) / TraceEvent.

### Файлы трассировки в проекте

| Файл                                      | Описание                                                                                                                                                                      |
| ----------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `iis_etw_events.ndjson`                   | Захваченные ETW-события в формате NDJSON (по одному JSON-объекту на строку). Содержит события от провайдеров `MSNT_SystemTrace` и `Microsoft-Windows-IIS-Configuration`.      |
| `Microsoft-Windows-IIS-Configuration.csv` | Справочник всех Event ID провайдера: ID, уровень, канал, шаблон сообщения и поля.                                                                                             |
| `Microsoft-Windows-IIS-Configuration.xml` | ETW-манифест провайдера в формате `instrumentationManifest`. Описывает ключевые слова (`Read`/`Write`), карты значений (`ErrorType`, `ChangeListenerType`) и шаблоны событий. |

## Структура проекта

```
WebApplication/
├── Default.aspx                                — стартовая страница: 2 таба (VPP + Deserialization)
├── LabVirtualPathProvider.cs                   — VPP, VirtualFile и состояние (LabVppState)
├── DeserializationLab.cs                       — 4 sink'а, ProcessScanner, бенчмарки безопасных payload'ов
├── Global.asax / Global.asax.cs                — точка входа приложения
├── Web.config                                  — конфигурация IIS и ASP.NET
├── iis_etw_events.ndjson                       — захваченные ETW-события (NDJSON)
├── Microsoft-Windows-IIS-Configuration.csv     — справочник Event ID провайдера IIS-Configuration
├── Microsoft-Windows-IIS-Configuration.xml     — ETW-манифест провайдера
├── README.md                                   — английская версия
├── README_RU.md                                — этот файл
└── AGENTS.md                                   — контракт для автоматизированных агентов
```

## Deserialization Lab (таб 2)

Второй таб `Default.aspx` — намеренно уязвимая песочница для валидации детектора
[CVEonDeserializationFinder](../CVEonDeserializationFinder). Хостится **только на
`http://localhost:8088`** (порт `8080` на этой рабочей станции занимает `gontlm-proxy.exe`).

**Предупреждение.** Приложение целенаправленно принимает произвольные payload-ы и передаёт их в
опасные deserializer'ы. Не разворачивайте его туда, где до него можно достучаться по сети.

### Как поднять сайт (elevated)

Из репозитория `CVEonDeserializationFinder`:

```powershell
scripts\install-iis.ps1            # включает полный IIS с ASP.NET 4.5
scripts\configure-iis-site.ps1     # создаёт AppPool + сайт CVEDeserializationLab на :8088
```

Скрипт `configure-iis-site.ps1` задаёт AppPool `CVEDeserializationLab` (.NET 4.0 Integrated,
LocalSystem — ради удобства WMI-запросов) и физический путь на
`E:\Documents\GitHub\WebApplication\WebApplication`.

### Как выглядит таб

1. **Выбор sink'а** — `RadioButtonList` с четырьмя pandroverami:
   - `BinaryFormatter` — `System.Runtime.Serialization.Formatters.Binary`
   - `XmlSerializer` — форма ToolShell / CVE-2025-53770:
     `List<ExpandedWrapper<LosFormatter, ObjectDataProvider>>`
   - `LosFormatter` — `System.Web.UI.LosFormatter`
   - `ObjectStateFormatter` — default ASP.NET ViewState
2. **Base64-payload** — `<asp:TextBox TextMode="MultiLine">`. Сюда вставляется вывод
   `ysoserial.exe -o base64` или `PayloadGenerator benign --sink <..>`.
3. **Кнопка «Load benign example»** — заполняет textbox готовым безопасным base64,
   встроенным в `DeserializationLab.cs`.
4. **Кнопка «Deserialize»** — декодирует base64, передаёт байты в выбранный sink, ловит любое
   исключение и записывает результат в скользящее окно «Recent attempts» (показывается тут же
   таблицей).
5. **Таблица дочерних процессов** (`ManagementObjectSearcher` по WMI `Win32_Process WHERE
ParentProcessId=<my pid>`) — обновляется по кнопке. Строки с именем из allow-list опасных
   команд (`ping.exe`, `cmd.exe`, `powershell.exe`, `mshta.exe`, `certutil.exe`, ...)
   подсвечиваются красным и в баннере пишется «SUSPICIOUS CHILD PROCESS DETECTED». Это
   каноническое доказательство того, что десериализация действительно исполнила код.

### Sink → gadget-соответствия для valid'ации

Из репозитория `CVEonDeserializationFinder`:

```powershell
$pg = 'src\CVEonDeserializationFinder.PayloadGenerator\bin\Debug\CVEonDeserializationFinder.PayloadGenerator.exe'

# Безопасные (в WebApp кнопка "Load benign example" даст то же самое).
& $pg demo

# Вредоносные — только внутри авторизованной лаборатории.
& $pg malicious --sink bf  --cmd "ping ya.ru -n 10"    # BinaryFormatter + TypeConfuseDelegate
& $pg malicious --sink xml --cmd "ping ya.ru -n 10"    # XmlSerializer/Xaml + ObjectDataProvider
& $pg malicious --sink los --cmd "ping ya.ru -n 10"    # LosFormatter + ActivitySurrogateSelector
& $pg malicious --sink osf --cmd "ping ya.ru -n 10"    # ObjectStateFormatter + ObjectDataProvider
```

### Ожидаемый результат по end-to-end

1. Провайдер AMSI зарегистрирован (`scripts\install-elevated.ps1`).
2. Payload вставлен и нажат «Deserialize».
3. **В `%ProgramData%\CVEonDeserializationFinder\hits.log`** появляется строка `"level":"hit"`
   с ID соответствующего правила (например,
   `GADGET-ExpandedWrapper-LosFormatter-ObjectDataProvider` для sink'а `xml`).
4. **В `%ProgramData%\CVEonDeserializationFinder\dumps\<yyyyMMdd-HHmmss.fff>_<pid>_w3wp\`**
   появляется папка с сырыми байтами сборки и JSON-sidecar'ом.
5. **В таблице дочерних процессов** веб-интерфейса появляется свежий `ping.exe` с parent =
   PID w3wp — подсвечен красным.
6. В логе `Recent attempts` записан «exception» (payload обычно бросает исключение уже после
   исполнения гаджета — это нормально).

### Зависимости `WebApplication.csproj`

Помимо стандартных ссылок, для лаборатории добавлены:

- `System.Runtime.Serialization` — `BinaryFormatter`.
- `System.Data.Services` + `System.Data.Services.Client` — `ExpandedWrapper<,>`.
- `WindowsBase` + `PresentationCore` + `PresentationFramework` — `ObjectDataProvider`.
- `System.Management` — WMI-запрос к дочерним процессам.

Соответствующие сборки прописаны в `Web.config` в блоке `<system.web><compilation><assemblies>`,
чтобы inline `<script runat="server">` в `Default.aspx` мог их резолвить.

## Связанные документы

- [`README.md`](README.md) — английская версия этого документа, структурно закреплена.
- [`AGENTS.md`](AGENTS.md) — контракт для автоматизированных агентов внутри этого репозитория.
- [`../CVEonDeserializationFinder/README_RU.md`](../CVEonDeserializationFinder/README_RU.md) —
  описание детектора (companion repo).
