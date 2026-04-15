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

| Возможность | Где используется |
|---|---|
| **Default Document** | `Web.config` → `<defaultDocument>` направляет корневой запрос `/` на `Default.aspx` без явного указания файла. |
| **Виртуальные пути** | `LabVirtualPathProvider` перехватывает запрос к `~/googlecheck.aspx`, хотя файла на диске не существует. IIS передаёт обработку ASP.NET, а тот — зарегистрированному `VirtualPathProvider`. |
| **Динамическая компиляция страниц** | ASP.NET компилирует `.aspx`-разметку, возвращённую `LabVirtualFile.Open()`, в IL-код «на лету», как если бы файл лежал на диске. |
| **Цепочка провайдеров** | `LabVirtualPathProvider` делегирует запросы предыдущему провайдеру через свойство `Previous`, сохраняя стандартное поведение для всех остальных путей. |
| **`GetCacheDependency`** | Переопределение возвращает `null` для виртуального пути, предотвращая попытку `FileChangesMonitor` отслеживать несуществующий физический каталог. |
| **`MemoryBuildResultCache`** | In-memory кэш скомпилированных страниц; очищается при рестарте AppDomain. |
| **`DiskBuildResultCache`** | На полном IIS скомпилированные `.aspx` сохраняются на диск в `Temporary ASP.NET Files` (`HttpRuntime.CodegenDir`). Этот кэш **переживает** рестарт AppDomain и пула приложений — именно поэтому виртуальная страница продолжала работать после деактивации. Для полной очистки необходимо удалить файлы из этой папки. |

### CLR / .NET Framework

| Возможность | Где используется |
|---|---|
| **`HostingEnvironment.RegisterVirtualPathProvider`** | Регистрация кастомного провайдера в рантайме по нажатию кнопки — без перезапуска AppDomain. Публичного API для снятия регистрации нет. |
| **`HttpRuntime.UnloadAppDomain`** | Единственный надёжный способ «разрегистрировать» VPP — перезапустить AppDomain, что очищает `MemoryBuildResultCache` и всю цепочку провайдеров. |
| **`HttpRuntime.CodegenDir`** | Путь к папке `Temporary ASP.NET Files` для текущего приложения. Используется для программной очистки дискового кэша компиляции. |
| **`volatile` поля** | `LabVppState.Registered` и `LabVppState.Active` объявлены `volatile`, что гарантирует видимость изменений между потоками ASP.NET thread pool. |
| **`VirtualFile` / `VirtualPathProvider`** | Абстрактные классы из `System.Web.Hosting`, позволяющие подменять файловую систему для ASP.NET. |
| **`HttpWebRequest`** | Виртуальная страница выполняет синхронный HTTP-запрос к `google.com/generate_204` для проверки внешней сетевой доступности. |

### C#

| Возможность | Где используется |
|---|---|
| **Expression-bodied members** | `private VirtualPathProvider PrevOrNull => Previous;` — свойство с expression body. |
| **Inline ASP.NET разметка** | Содержимое виртуальной `.aspx`-страницы формируется как verbatim-строка (`@"..."`) и отдаётся через `MemoryStream`. |
| **`sealed` классы** | `LabVirtualPathProvider` и `LabVirtualFile` помечены `sealed`, предотвращая нежелательное наследование и позволяя JIT-оптимизации девиртуализовать вызовы. |
| **`static readonly` vs `const`** | Токен хранится как `static readonly string`, а не `const`, чтобы его можно было подменить через рефлексию в тестах без перекомпиляции зависимых сборок. |

## Обнаружение через ETW (Event Tracing for Windows)

Использование `VirtualPathProvider` может быть обнаружено на стороне IIS при помощи провайдера ETW **`Microsoft-Windows-IIS-Configuration`** (`{dc0b8e51-4863-407a-bc3c-1b479b2978ac}`). В проект включены артефакты трассировки, демонстрирующие этот процесс.

### Ключевые ETW-события

| Event ID | Уровень | Канал | Описание | Индикатор VPP |
|---|---|---|---|---|
| **25** | Verbose | Analytic | Виртуальный путь `{ConfigPath}` сопоставлен с физическим путём `{PhysicalPath}`. | Физический путь указывает на файл, **которого нет** на диске (`googlecheck.aspx`). |
| **47** | Verbose | Analytic | Папка конфигурации `{ConfigPath}` сопоставлена с каталогом `{Directory}`. | IIS пытается найти `web.config` рядом с несуществующим файлом. |
| **28** | Info | Debug | Олицетворение маркера доступа `{ImpersonationTokenHandle}`. | Сопровождает обращение к виртуальному пути — фиксирует контекст безопасности. |
| **13** | Verbose | Analytic | Анализ файла конфигурации `{PhysicalPath}`. | Показывает цепочку конфигурации, по которой IIS дошёл до виртуального пути. |
| **17 / 18 / 19 / 21** | Verbose | Debug | Мониторы файловых изменений: создание, ожидание, уведомление, удаление. | IIS создаёт `FileChangeNotificationMonitor` для каталогов, включая пути к виртуальным файлам. |
| **9** | Verbose | Debug | Сброс кэша конфигурации для `{ConfigPath}` и вложенных путей. | Срабатывает при `HttpRuntime.UnloadAppDomain()` — признак деактивации VPP. |

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

| Файл | Описание |
|---|---|
| `iis_etw_events.ndjson` | Захваченные ETW-события в формате NDJSON (по одному JSON-объекту на строку). Содержит события от провайдеров `MSNT_SystemTrace` и `Microsoft-Windows-IIS-Configuration`. |
| `Microsoft-Windows-IIS-Configuration.csv` | Справочник всех Event ID провайдера: ID, уровень, канал, шаблон сообщения и поля. |
| `Microsoft-Windows-IIS-Configuration.xml` | ETW-манифест провайдера в формате `instrumentationManifest`. Описывает ключевые слова (`Read`/`Write`), карты значений (`ErrorType`, `ChangeListenerType`) и шаблоны событий. |

## Структура проекта

```
WebApplication/
├── Default.aspx                                — стартовая страница с кнопками управления VPP
├── LabVirtualPathProvider.cs                   — VPP, VirtualFile и состояние (LabVppState)
├── Global.asax / Global.asax.cs                — точка входа приложения
├── Web.config                                  — конфигурация IIS и ASP.NET
├── iis_etw_events.ndjson                       — захваченные ETW-события (NDJSON)
├── Microsoft-Windows-IIS-Configuration.csv     — справочник Event ID провайдера IIS-Configuration
├── Microsoft-Windows-IIS-Configuration.xml     — ETW-манифест провайдера
└── README.md                                   — этот файл
```
