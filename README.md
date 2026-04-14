# VirtualPathProvider Lab

Лабораторная работа демонстрирует динамическую генерацию виртуальных страниц средствами ASP.NET, IIS и CLR.

## Как запустить

1. Откройте решение в Visual Studio и запустите проект (F5 / IIS Express).
2. На стартовой странице (`Default.aspx`) нажмите **Register VPP (and activate)**.
3. Перейдите по ссылке, отображённой в статусе (`/vpp/googlecheck.aspx?token=labtoken-change-me`).
4. Виртуальная страница проверит доступность `google.com` и вернёт результат в виде текста.
5. Кнопка **Deactivate (AND unregister)** сбрасывает VPP и перезапускает AppDomain через `HttpRuntime.UnloadAppDomain()`, полностью очищая кэш компиляции. После этого виртуальная страница возвращает 404.

## Продемонстрированные возможности

### IIS / ASP.NET pipeline

| Возможность | Где используется |
|---|---|
| **Default Document** | `Web.config` → `<defaultDocument>` направляет корневой запрос `/` на `Default.aspx` без явного указания файла. |
| **Виртуальные пути** | `LabVirtualPathProvider` перехватывает запрос к `~/vpp/googlecheck.aspx`, хотя файла на диске не существует. IIS передаёт обработку ASP.NET, а тот — зарегистрированному `VirtualPathProvider`. |
| **Динамическая компиляция страниц** | ASP.NET компилирует `.aspx`-разметку, возвращённую `LabVirtualFile.Open()`, в IL-код «на лету», как если бы файл лежал на диске. |
| **Цепочка провайдеров** | `LabVirtualPathProvider` делегирует запросы предыдущему провайдеру через свойство `Previous`, сохраняя стандартное поведение для всех остальных путей. |
| **`GetCacheDependency`** | Переопределение возвращает `null` для виртуального пути, предотвращая попытку `FileChangesMonitor` отслеживать несуществующий физический каталог. |
| **`MemoryBuildResultCache`** | ASP.NET кэширует скомпилированные виртуальные страницы; простое выключение флага `Active` не удаляет страницу из кэша — требуется перезапуск AppDomain. |

### CLR / .NET Framework

| Возможность | Где используется |
|---|---|
| **`HostingEnvironment.RegisterVirtualPathProvider`** | Регистрация кастомного провайдера в рантайме по нажатию кнопки — без перезапуска AppDomain. Публичного API для снятия регистрации нет. |
| **`HttpRuntime.UnloadAppDomain`** | Единственный надёжный способ «разрегистрировать» VPP — перезапустить AppDomain, что очищает `MemoryBuildResultCache` и всю цепочку провайдеров. |
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

## Структура проекта

```
WebApplication/
├── Default.aspx                  — стартовая страница с кнопками управления VPP
├── LabVirtualPathProvider.cs     — VPP, VirtualFile и состояние (LabVppState)
├── Global.asax / Global.asax.cs  — точка входа приложения
├── Web.config                    — конфигурация IIS и ASP.NET
└── README.md                     — этот файл
```
