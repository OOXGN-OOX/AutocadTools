# Установка Autocad Tools

[← Главная страница](../README.md)

## Установка

1. Соберите `AutocadTools-0.1.0-Setup.exe` по инструкции в [руководстве по сборке](DEVELOPMENT.md#bundle-и-установщик) или скачайте установщик из [GitHub Releases](https://github.com/OOXGN-OOX/AutocadTools/releases), когда он будет опубликован.
2. Закройте AutoCAD и Core Console.
3. Запустите установщик. Для установки всем пользователям потребуются права администратора; установка только для себя не требует повышения прав.
4. Запустите AutoCAD. В ленте появится вкладка **Autocad Tools**.

В мастере выберите установку для всех пользователей (с правами администратора) или только для себя. Путь определяется автоматически. Для текущего пользователя используется `%APPDATA%\Autodesk\ApplicationPlugins\AutocadTools.bundle`.

Стандартная папка общей установки:

```text
C:\Program Files\Autodesk\ApplicationPlugins\AutocadTools.bundle
```

Установщик выбирает DLL через манифест bundle: линия .NET 8 для AutoCAD 2025–2026, линия .NET 10 для 2027. Он не устанавливает AutoCAD и не отключает `SECURELOAD`. При необходимости проверьте `APPAUTOLOAD`, доверенный путь и сообщения AutoCAD.

Для ручной установки распакуйте ZIP с bundle и скопируйте `AutocadTools.bundle` в стандартную папку `ApplicationPlugins`. Для отдельной проверки через `NETLOAD` выбирайте DLL именно вашей линии:

```text
Contents\Windows\2025-2026\AutocadTools.dll
Contents\Windows\2027\AutocadTools.dll
```

Не загружайте обе DLL одновременно. Установка создаёт отдельный bundle и настройки в `%LOCALAPPDATA%\AutocadTools`.

### Обновление и удаление

Для обновления закройте AutoCAD и запустите новый установщик. Он удалит установленную версию из выбранного каталога через её деинсталлятор, затем установит новую. При другой папке или режиме установки мастер укажет найденную копию; удалите её через Windows. Удалить плагин можно через «Установленные приложения» Windows или `unins000.exe` в папке bundle. Настройки в `%LOCALAPPDATA%\AutocadTools` и `HKCU\Software\OOXGN\AutocadTools\AutoNum` сохраняются при удалении.
