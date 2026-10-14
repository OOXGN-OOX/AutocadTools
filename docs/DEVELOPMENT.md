# Сборка и разработка

[← Главная страница](../README.md)

## Совместимость

| AutoCAD | Целевой профиль | Проверка |
|---|---|---|
| 2025 | .NET 8, API 25.0.1 | Компиляция; загрузка в полном приложении ожидает проверки |
| 2026 | .NET 8, API 25.1.0 | Компиляция; загрузка в полном приложении ожидает проверки |
| 2027 | .NET 10, API 26.0.0 | Компиляция; загрузка в полном приложении ожидает проверки |

AutoCAD 2027 требует перекомпиляции относительно 2025/2026, поэтому пакет содержит разные DLL. Обновления AutoCAD 2025/2026 также могут менять версию .NET хоста; используйте актуальные обновления Autodesk и проверяйте загрузку на своей установленной версии. [Совместимость Autodesk](https://help.autodesk.com/cloudhelp/2027/ENU/AutoCAD-Customization/files/GUID-A6C680F2-DE2E-418A-A182-E4884073338A.htm).

AutoCAD LT и другие CAD-платформы в текущую матрицу проверок не входят.

## Сборка из исходников

Для полного дистрибутива на Windows x64 достаточно одной команды:

```powershell
.\build.cmd
```

Можно также запустить `build.cmd` двойным щелчком или вызвать его по полному пути из другой папки. Скрипт работает в штатном Windows PowerShell 5.1 и передаёт код ошибки вызывающей оболочке.

Он проверяет структуру исходников, собирает Release DLL для обеих линий AutoCAD, добавляет шаблон форматки и уведомления о лицензиях, затем компилирует EXE и создаёт ZIP с контрольными суммами. Сборка **не устанавливает** плагин в AutoCAD.

При первом запуске требуется доступ к Microsoft, GitHub и NuGet. Если в PATH нет SDK .NET 10, скрипт загрузит SDK 10.0.402 с сайта Microsoft, проверит SHA512 и распакует его в `artifacts\tools\dotnet`. SDK .NET 10 собирает оба целевых профиля (.NET 8 и .NET 10). Если компилятор Inno Setup не найден, версия 6.7.3 загружается из официального выпуска с проверкой SHA256 и распаковывается в переносимом режиме в `artifacts\tools\InnoSetup-6.7.3`. Инструменты кешируются, системный PATH и регистрация приложений не меняются; права администратора не нужны.

### Отдельная DLL

Для ручного вызова `dotnet` нужен установленный SDK .NET 10 (для профилей 2025/2026 подходит также SDK .NET 8) и доступ к NuGet. По умолчанию используются DLL установленной версии AutoCAD. Для отсутствующих 2025/2026/2027 проект получает официальные compile-only пакеты Autodesk; Autodesk DLL не включаются в дистрибутив. Принудительно выбрать NuGet можно параметром `-p:UseAutoCADNuGet=true`.

```powershell
git clone https://github.com/OOXGN-OOX/AutocadTools.git
cd AutocadTools
dotnet build .\AutocadTools.csproj -c Release -p:AutoCADVersion=2025
dotnet build .\AutocadTools.csproj -c Release -p:AutoCADVersion=2026
dotnet build .\AutocadTools.csproj -c Release -p:AutoCADVersion=2027
```

Если AutoCAD установлен в другом месте, добавьте `-p:AutoCADPath="D:\Autodesk\AutoCAD 2027"`. Сборка создаёт локальные файлы и **не устанавливает** плагин в AutoCAD.

### Bundle и установщик

Основная команда `build.cmd` вызывает `Build-Installer.ps1`. Путь к компилятору указывать не требуется. При необходимости доступны отдельные сценарии:

```powershell
.\scripts\Build-Bundle.ps1
.\scripts\Build-Installer.ps1
```

`Build-Bundle.ps1` использует `dotnet` из PATH. Полная сборка автоматически получает API Autodesk из NuGet независимо от установленных версий AutoCAD. Для использования собственных SDK/DLL или компилятора передайте параметры:

```powershell
.\build.cmd -AutoCAD2025Path "D:\Autodesk\AutoCAD 2025" -AutoCAD2027Path "D:\Autodesk\AutoCAD 2027"
.\scripts\Build-Installer.ps1 -CompilerPath "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
```

Компилятор: Inno Setup 6.7.3 или совместимая версия из [официального источника](https://jrsoftware.org/isdl.php). Автоматический поиск проверяет локальный кеш, PATH и стандартные папки установки Inno Setup 6/7. Скрипт установщика проверяет наличие обеих линий DLL и запрещает включение Autodesk host DLL. Результаты находятся в `artifacts/release`: `AutocadTools-<версия>-Setup.exe`, `AutocadTools-<версия>-Bundle.zip` и `SHA256SUMS.txt`. Версия берётся из `AutocadTools.csproj` и переносится в манифест bundle.

Для упаковки уже готового bundle укажите `-BundlePath`; он должен содержать обе линии DLL, шаблоны и папку `ThirdPartyNotices`. В этом режиме SDK и повторная компиляция не нужны.

GitHub Actions также запускает `build.cmd` и сохраняет дистрибутив в артефакт `AutocadTools-installer` после проверок.

Повторяемость означает сборку из фиксированных исходников, версий зависимостей и сценария установщика. Побайтовая идентичность EXE между разными датами/компиляторами не заявляется; контрольные суммы относятся к конкретному выпуску.

### Проверки

```powershell
.\scripts\Verify-Structure.ps1
dotnet run --project .\tests\PdfChecks\PdfChecks.csproj -c Release
dotnet run --project .\tests\UpdateChecks\UpdateChecks.csproj -c Release -f net8.0
dotnet run --project .\tests\UpdateChecks\UpdateChecks.csproj -c Release -f net10.0
```

Отдельные тесты загрузки выполняются через `tests/AutoCADSmoke` в AutoCAD 2026 или 2027 Core Console. Состав и границы проверок — в [VALIDATION.md](VALIDATION.md).
