namespace OpenNetMeter.PlatformAbstractions;

public interface IStartupRegistrationService
{
    bool IsEnabled();

    /// <summary>
    /// 校验已注册的自启任务是否仍指向当前可执行文件；不一致时按当前路径重写。
    /// 程序被移动/重命名/重新解压到别的目录后，旧路径会让登录启动静默失败（任务结果 0x80070002）。
    /// </summary>
    void EnsureUpToDate(bool startMinimized);

    void SetEnabled(bool enabled, bool startMinimized);
}
