// RegistryManager.cs - IMEJapanese
#nullable enable
using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace IMEJapanese
{
    #region [ 레지스트리 키맵핑 도구 (RegistryManager) ]
    internal static class RegistryManager
    {
        private const string RegPath = @"SYSTEM\CurrentControlSet\Control\Keyboard Layout";
        private const string RegValue = "Scancode Map";
        // [수정 #14] 앱 설정 저장/로드용 레지스트리 경로 (HKCU)
        private const string SettingsRegPath = @"SOFTWARE\IMEJapanese\Settings";
        private static readonly byte[] MappingBytes = { 0x71, 0xE0, 0x6E, 0x00 };

        public static bool IsAdmin()
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }

        public static bool IsMappingApplied()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(RegPath, false);
                if (key?.GetValue(RegValue) is byte[] data && data.Length >= 20)
                {
                    int count = BitConverter.ToInt32(data, 8);
                    for (int i = 0; i < count - 1; i++)
                    {
                        int offset = 12 + (i * 4);
                        if (offset + 4 <= data.Length)
                        {
                            if (data[offset] == MappingBytes[0] && data[offset + 1] == MappingBytes[1] &&
                                data[offset + 2] == MappingBytes[2] && data[offset + 3] == MappingBytes[3])
                                return true;
                        }
                    }
                }
                return false;
            }
            catch { return false; }
        }

        public static bool ToggleMapping(bool apply)
        {
            if (!IsAdmin())
            {
                MessageBox.Show("레지스트리 수정을 위해 앱을 '관리자 권한'으로 실행해주세요.", "권한 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(RegPath, true);
                if (key == null) return false;
                byte[]? currentData = key.GetValue(RegValue) as byte[];

                if (apply)
                {
                    if (IsMappingApplied()) return true;

                    byte[] newData;
                    if (currentData == null || currentData.Length < 20)
                    {
                        newData = new byte[20];
                        Array.Clear(newData, 0, 8);
                        BitConverter.GetBytes(2).CopyTo(newData, 8);
                        MappingBytes.CopyTo(newData, 12);
                    }
                    else
                    {
                        int oldCount = BitConverter.ToInt32(currentData, 8);
                        newData = new byte[currentData.Length + 4];
                        Array.Copy(currentData, 0, newData, 0, 8);
                        BitConverter.GetBytes(oldCount + 1).CopyTo(newData, 8);
                        Array.Copy(currentData, 12, newData, 12, currentData.Length - 16);
                        MappingBytes.CopyTo(newData, currentData.Length - 4);
                    }
                    key.SetValue(RegValue, newData, RegistryValueKind.Binary);
                }
                else
                {
                    if (!IsMappingApplied() || currentData == null) return true;

                    int oldCount = BitConverter.ToInt32(currentData, 8);
                    if (oldCount <= 2) key.DeleteValue(RegValue, false);
                    else
                    {
                        byte[] newData = new byte[currentData.Length - 4];
                        Array.Copy(currentData, 0, newData, 0, 8);
                        BitConverter.GetBytes(oldCount - 1).CopyTo(newData, 8);

                        int destOffset = 12;
                        for (int i = 0; i < oldCount - 1; i++)
                        {
                            int srcOffset = 12 + (i * 4);
                            if (!(currentData[srcOffset] == MappingBytes[0] && currentData[srcOffset + 1] == MappingBytes[1] &&
                                  currentData[srcOffset + 2] == MappingBytes[2] && currentData[srcOffset + 3] == MappingBytes[3]))
                            {
                                Array.Copy(currentData, srcOffset, newData, destOffset, 4);
                                destOffset += 4;
                            }
                        }
                        key.SetValue(RegValue, newData, RegistryValueKind.Binary);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"레지스트리 수정 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        // ─────────────────────────────────────────────────
        // [수정 #14] 앱 사용자 설정 영구 저장 / 로드
        // HKCU\SOFTWARE\IMEJapanese\Settings 에 저장
        // ─────────────────────────────────────────────────

        /// <summary>현재 AppConfig 설정값을 레지스트리에 저장합니다.</summary>
        public static void SaveSettings()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(SettingsRegPath, true);
                if (key == null) return;
                key.SetValue("UseGoogleApi",          AppConfig.UseGoogleApi          ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("DefaultCapsMode",       AppConfig.DefaultCapsMode,               RegistryValueKind.DWord);
                key.SetValue("ShowTextOverlay",        AppConfig.DefaultShowTextOverlay  ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("ShowKeyboardLayout",     AppConfig.DefaultShowKeyboardLayout ? 1 : 0, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                if (AppConfig.LogLevel >= 1) System.Diagnostics.Debug.WriteLine($"[RegistryManager] 설정 저장 실패: {ex.Message}");
            }
        }

        /// <summary>레지스트리에서 앱 설정값을 읽어 AppConfig에 적용합니다.</summary>
        public static void LoadSettings()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(SettingsRegPath, false);
                if (key == null) return;

                if (key.GetValue("UseGoogleApi") is int useGoogle)
                    AppConfig.UseGoogleApi = useGoogle != 0;

                if (key.GetValue("DefaultCapsMode") is int capsMode && capsMode >= 1 && capsMode <= 3)
                    AppConfig.DefaultCapsMode = capsMode;

                if (key.GetValue("ShowTextOverlay") is int showOverlay)
                    AppConfig.DefaultShowTextOverlay = showOverlay != 0;

                if (key.GetValue("ShowKeyboardLayout") is int showLayout)
                    AppConfig.DefaultShowKeyboardLayout = showLayout != 0;
            }
            catch (Exception ex)
            {
                if (AppConfig.LogLevel >= 1) System.Diagnostics.Debug.WriteLine($"[RegistryManager] 설정 로드 실패: {ex.Message}");
            }
        }
    }
    #endregion
}
