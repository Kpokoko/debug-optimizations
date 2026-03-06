# Подготовка
Обновить или установить .NET 7
* [Рантайм](https://dotnet.microsoft.com/ru-ru/download/dotnet/thank-you/runtime-aspnetcore-7.0.20-windows-x64-installer)
* [SDK](https://dotnet.microsoft.com/ru-ru/download/dotnet/thank-you/sdk-7.0.410-windows-x64-installer)

Поставить resharper для студии или rider, dotTrace, dotMemory с сайта jetbrains(Если вдруг нет лицензии, то можете [получить её как студенты](https://www.jetbrains.com/community/education/#students)). Ставьте последние доступные версии и пользуйтесь VPN

Установить winDbg
* Установи вот [отсюда](https://www.microsoft.com/en-us/p/windbg/9pgjgd53tn86#activetab=pivot:overviewtab)
* Скачай [dump](https://drive.google.com/file/d/12DxOIzWBZWZOWWx_Hs2lH9doLY-XiyfH/view?usp=sharing) для теста
* Проверь, что всё ок. Для этого пройди по [инструкции](https://docs.google.com/document/d/1IQdwg7D7HitPcUBrjypwblUXNfVPPwZVjEW7vDU2hnI/edit?usp=sharing)
* Скачай [sosex](https://drive.google.com/file/d/1j89F-M7GGdpQh7RQrllUDJINeoIkJN5j/view?usp=sharing), если вдруг у тебя его нет. В инструкции написано что с ним делать и для чего он нужен

# Домашнее задание
## Основное задание
Вам дан код JPEG подобного сжатия (проект JPEG), вам нужно максимально, насколько это возможно, оптимизировать его, в том числе уменьшить потребление памяти.

Рекомендации:
* Профилируйте код (используйте dotTrace)
* Для начала оптимизируйте загрузку изображений и переписывайте только неэффективный код
* Пишите бенчмарки на разные методы
* Не бойтесь математики

С разными вопросами можно писать @Golrans и @ryzhes

Подсказки:
* Распаралельте DCT
* CbCr subsampling
* Используйте указатели, вместо GetPixel/SetPixel, придётся написать unsafe код
* Замените DCT на FFT (System.Numerics.Complex), нельзя использовать библиотеки, только собственная реализация!
* Помимо подсказанного в проекте ещё много узких мест (╯°□°）╯︵ ┻━┻

Как сдавать задание:
1. Нужно сделать замер через JpegProcessorBenchmark до оптимизаций и запомнить Mean и Allocated по операциям Compress и Uncompress
2. Сделать аналогичные замеры после оптимизаций
3. Внести свой результат в таблицу в день дедлайна, ссылку на которую вам дадут позже
4. Очно или онлайн за 10-15 минут рассказать какие моменты удалось найти и как оптимизировать

# Полезные ссылки
* [Про new()](https://devblogs.microsoft.com/premier-developer/dissecting-the-new-constraint-in-c-a-perfect-example-of-a-leaky-abstraction/)
* [Про IEquatable](https://devblogs.microsoft.com/premier-developer/performance-implications-of-default-struct-equality-in-c/)
* [Про Inlining методов](https://web.archive.org/web/20200108171755/http://blogs.microsoft.co.il/sasha/2012/01/20/aggressive-inlining-in-the-clr-45-jit/)
* [Про сборку мусора](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/)
* [dotnet diagnostic tutorials от Microsoft](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/debug-memory-leak)

Презентации:
* [Отладка](https://docs.google.com/presentation/d/1X8A1Ly0PuNBIPXOoqkG6dVk8mt4tdoYUeZJTzXYoUmw/edit?usp=sharing)
* [Оптимизация](https://docs.google.com/presentation/d/1kZEkRFLmUI9NYepbqTIT0FV8dGnt_96EsTgdp85hi1Y/edit?usp=sharing)

WinDbg:
* [WinDbg commands](https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/commands)
* [sos commands](http://www.windbg.xyz/windbg/article/10-SOS-Extension-Commands)
* [sosex commands](https://knowledge-base.havit.eu/tag/windbg/)
