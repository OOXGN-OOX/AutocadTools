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

Нужны Windows x64, SDK .NET 8 и .NET 10, доступ к NuGet. По умолчанию используются DLL установленной версии AutoCAD. Для отсутствующих 2025/2026/2027 проект получает официальные compile-only пакеты Autodesk; Autodesk DLL не включаются в дистрибутив. Принудительно выбрать NuGet можно параметром `-p:UseAutoCADNuGet=true`.

```powershell
git clone https://github.com/OOXGN-OOX/AutocadTools.git
cd AutocadTools
dotnet build .\AutocadTools.csproj -c Release -p:AutoCADVersion=2025
dotnet build .\AutocadTools.csproj -c Release -p:AutoCADVersion=2026
dotnet build .\AutocadTools.csproj -c Release -p:AutoCADVersion=2027
```

Если AutoCAD установлен в другом месте, добавьте `-p:AutoCADPath="D:\Autodesk\AutoCAD 2027"`. Сборка создаёт локальные файлы и **не устанавливает** плагин в AutoCAD.

### Bundle и установщик

```powershell
.\scripts\Build-Bundle.ps1
.\scripts\Build-Installer.ps1 -CompilerPath "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
```

Компилятор: Inno Setup 6.7.3 или совместимая версия из [официального источника](https://jrsoftware.org/isdl.php). Скрипт установщика проверяет наличие обеих линий DLL и запрещает включение Autodesk host DLL. Результаты находятся в `artifacts/release`: EXE, ZIP с bundle и `SHA256SUMS.txt`.

Повторяемость означает сборку из фиксированных исходников, версий зависимостей и сценария установщика. Побайтовая идентичность EXE между разными датами/компиляторами не заявляется; контрольные суммы относятся к конкретному выпуску.

### Проверки

```powershell
.\scripts\Verify-Structure.ps1
dotnet run --project .\tests\PdfChecks\PdfChecks.csproj -c Release
dotnet run --project .\tests\UpdateChecks\UpdateChecks.csproj -c Release -f net8.0
dotnet run --project .\tests\UpdateChecks\UpdateChecks.csproj -c Release -f net10.0
```

Отдельные тесты загрузки выполняются через `tests/AutoCADSmoke` в AutoCAD 2026 или 2027 Core Console. Состав и границы проверок — в [VALIDATION.md](VALIDATION.md).
