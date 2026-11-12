# Writes the two string tables, Strings/en-US and Strings/ru-RU Resources.resw, from the
# one list below, so a string and its translation stay side by side.
#
#   pwsh tools/make-strings.ps1
#
# Format of a line: Key | English | Russian. The generated .resw files are committed.

$strings = @'
AppName | btManage | btManage
UnknownApple | Apple headphones | Наушники Apple
UnknownFastPair | Fast Pair earbuds | Наушники Fast Pair

KindEarbuds | Earbuds | Наушники
KindHeadphones | Headphones | Наушники
KindHeadset | Headset | Гарнитура
KindSpeaker | Speaker | Колонка
KindMouse | Mouse | Мышь
KindKeyboard | Keyboard | Клавиатура
KindGamepad | Game controller | Геймпад
KindRemote | Remote | Пульт
KindPhone | Phone | Телефон
KindTablet | Tablet | Планшет
KindComputer | Computer | Компьютер
KindWatch | Watch | Часы
KindWearable | Wearable | Носимое устройство
KindCar | Car | Автомобиль
KindOther | Bluetooth device | Устройство Bluetooth

RoleLeft | Left | Левый
RoleRight | Right | Правый
RoleCase | Case | Кейс
RoleMain | Battery | Заряд
InEar | in ear | в ухе
Charging | charging | заряжается
PercentFormat | {0}% | {0} %

StatusConnected | Connected | Подключено
StatusNearby | Nearby | Рядом
StatusDisconnected | Not connected | Не подключено
AgeMinutes | {0} min ago | {0} мин назад
AgeHours | {0} h ago | {0} ч назад
AgeDays | {0} d ago | {0} дн назад
EstimateHoursMinutes | About {0} h {1} min left | Хватит примерно на {0} ч {1} мин
EstimateHours | About {0} h left | Хватит примерно на {0} ч
EstimateMinutes | About {0} min left | Хватит примерно на {0} мин

EmptyTitle | No active devices | Нет активных устройств
EmptyHint | Connected devices and earbuds nearby show up here | Здесь появятся подключённые устройства и наушники поблизости
BluetoothOffTitle | Bluetooth is off | Bluetooth выключен
BluetoothOffHint | Turn Bluetooth on to see your devices | Включите Bluetooth, чтобы видеть устройства
BluetoothMissingTitle | No Bluetooth adapter | Нет адаптера Bluetooth
BluetoothMissingHint | This computer has no Bluetooth adapter | На этом компьютере не найден адаптер Bluetooth
TurnOnBluetooth | Turn on Bluetooth | Включить Bluetooth
AllDevices | All devices | Все устройства
BluetoothSettings | Bluetooth settings | Параметры Bluetooth

MenuOpen | Open btManage | Открыть btManage
MenuExit | Exit | Выход

GroupConnected | Connected | Подключены
GroupNearby | Nearby | Рядом
GroupOther | Not connected | Не подключены
Settings | Settings | Параметры
NoDevicesTitle | No paired devices | Сопряжённых устройств нет
NoDevicesHint | Add a device in Windows Bluetooth settings | Добавьте устройство в параметрах Bluetooth Windows

Connect | Connect | Подключить
Disconnect | Disconnect | Отключить
More | More | Ещё
RemoveDevice | Remove device | Удалить устройство
ForgetHistory | Clear battery history | Очистить историю заряда
SectionBattery | Battery | Заряд
NoBattery | This device does not report its battery | Устройство не сообщает заряд
SectionHistory | Battery over 24 hours | Заряд за 24 часа
HistoryEmpty | The history shows up once there is data | История появится, когда накопятся данные
ChartNow | now | сейчас
SectionInfo | Details | Сведения
InfoType | Type | Тип
InfoModel | Model | Модель
InfoAddress | Address | Адрес
InfoTransport | Link | Соединение
TransportClassic | Bluetooth Classic | Bluetooth Classic
TransportLe | Bluetooth LE | Bluetooth LE
TransportDual | Classic and LE | Classic и LE
InfoProfiles | Profiles | Профили
InfoSignal | Signal | Сигнал
SignalFormat | {0} dBm | {0} дБм
InfoIds | Identifiers | Идентификаторы
InfoSource | Battery read from | Источник заряда
SourceAdvert | Bluetooth LE advertisements | Объявления Bluetooth LE
SourceSystem | Windows | Windows
SourceGatt | Battery Service | Служба батареи
SourceHid | HID report | HID-отчёт
SourceGamepad | Game controller interface | Интерфейс игровых контроллеров
InfoLastConnected | Last connected | Последнее подключение
DeviceNotifications | Notifications about this device | Уведомления об этом устройстве

RemoveDialogTitle | Remove {0}? | Удалить {0}?
RemoveDialogText | The pairing will be removed. To use the device again you will have to pair it anew | Сопряжение будет разорвано. Чтобы пользоваться устройством снова, его придётся подключить заново
Cancel | Cancel | Отмена
ActionFailed | That did not work | Не получилось
ActionFailedHint | Windows refused the request. Try from Windows Bluetooth settings | Windows отклонила запрос. Попробуйте через параметры Bluetooth Windows

SettingsGeneral | General | Общие
SettingAutostart | Start with Windows | Запускать вместе с Windows
SettingBluetooth | Bluetooth | Bluetooth
SettingsAppearance | Appearance | Оформление
SettingDesign | Design | Стиль интерфейса
DesignFluent | Fluent | Fluent
DesignMaterial | Material | Material
SettingTheme | Theme | Тема
ThemeSystem | Use system setting | Как в системе
ThemeLight | Light | Светлая
ThemeDark | Dark | Тёмная
SettingAccent | Accent colour | Акцентный цвет
AccentSystem | Use system setting | Как в системе
AccentBluetooth | Bluetooth blue | Синий Bluetooth
SettingBackdrop | Window backdrop | Фон окон
BackdropAuto | Automatic | Авто
BackdropAcrylic | Acrylic | Acrylic
BackdropMica | Mica | Mica
BackdropMicaAlt | Mica Alt | Mica Alt
SettingLanguage | Language | Язык
LanguageSystem | Use system setting | Как в системе
SettingRestartHint | The application restarts to apply this | Для применения приложение перезапустится
SettingsNotifications | Notifications | Уведомления
SettingNotifyArrival | When headphones connect or come near | Когда наушники подключаются или оказываются рядом
SettingNotifyLow | When a battery runs low | Когда заряд становится низким
SettingThreshold | Low battery threshold | Порог низкого заряда
SettingsControllers | Game controllers | Геймпады
SettingPlayStation | Read the battery of PlayStation controllers | Читать заряд геймпадов PlayStation
SettingPlayStationHint | Switches the controller to full reports. Games that read it through DirectInput may stop seeing it until it reconnects | Переводит геймпад в режим полных отчётов. Игры, которые читают его через DirectInput, могут перестать его видеть до переподключения
SettingsUpdates | Updates | Обновления
SettingAutoUpdate | Check for updates once a day | Проверять обновления раз в день
UpdateCheck | Check now | Проверить сейчас
UpdateChecking | Checking | Проверка
UpdateUpToDate | You have the latest version | Установлена последняя версия
UpdateAvailable | Version {0} is available | Доступна версия {0}
UpdateInstall | Install | Установить
UpdateDownloading | Downloading, {0}% | Загрузка, {0} %
UpdateCheckFailed | Could not check for updates | Не удалось проверить обновления
UpdateInstallFailed | Could not install it. Download it from the release page | Не удалось установить. Скачайте обновление со страницы выпуска
UpdateReleasePage | Release page | Страница выпуска
SettingsAbout | About | О программе
AboutVersion | Version {0}, core {1} | Версия {0}, ядро {1}

ToastLow | Low battery: {0} | Низкий заряд: {0}
'@

function Write-Resw([string]$language, [int]$column) {
    $builder = [System.Text.StringBuilder]::new()
    [void]$builder.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
    [void]$builder.AppendLine('<!-- Generated by tools/make-strings.ps1. Edit the list there, not this file. -->')
    [void]$builder.AppendLine('<root>')
    foreach ($header in @(
            @('resmimetype', 'text/microsoft-resx'),
            @('version', '2.0'),
            @('reader', 'System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089'),
            @('writer', 'System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089'))) {
        [void]$builder.AppendLine("  <resheader name=`"$($header[0])`"><value>$($header[1])</value></resheader>")
    }

    foreach ($line in $strings -split "`r?`n") {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $parts = $line -split '\s*\|\s*'
        if ($parts.Count -ne 3) { throw "Bad line: $line" }
        $value = [System.Security.SecurityElement]::Escape($parts[$column].Trim())
        # A no-break space keeps a number and its unit on one line.
        $value = $value -replace '\{0\} %', "{0}$([char]0x00A0)%"
        [void]$builder.AppendLine("  <data name=`"$($parts[0].Trim())`" xml:space=`"preserve`"><value>$value</value></data>")
    }

    [void]$builder.AppendLine('</root>')
    $path = Join-Path $PSScriptRoot "..\src\BtManage\Strings\$language\Resources.resw"
    [System.IO.File]::WriteAllText($path, $builder.ToString(), [System.Text.UTF8Encoding]::new($false))
    "Wrote $path"
}

Write-Resw 'en-US' 1
Write-Resw 'ru-RU' 2
