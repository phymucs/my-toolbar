using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;
using SolidWorksTools;
using CodeStack.Community.Sw.MyToolbar.Preferences;
using CodeStack.Community.Sw.MyToolbar.Helpers;
using System.IO;
using Newtonsoft.Json;
using CodeStack.Community.Sw.MyToolbar.Properties;
using System.Linq;
using SolidWorks.Interop.swconst;
using Xarial.Community.AppLaunchKit.Attributes;
using Xarial.Community.AppLaunchKit;

namespace CodeStack.Community.Sw.MyToolbar
{
    /// <summary>
    /// Summary description for my_toolbar.
    /// </summary>
    [Guid("63496b16-e9ad-4d3a-8473-99d124a1672b"), ComVisible(true)]
    [SwAddin(Description = "Add-in for managing custom toolbars", 
        Title = "MyToolbar", LoadAtStartup = true)]
    [ApplicationDirectory(System.Environment.SpecialFolder.ApplicationData, "CodeStack\\MyToolbar\\System")]
    [Eula(@"MIT License

Copyright (c) 2018 www.codestack.net

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the 'Software'), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and / or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED 'AS IS', WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
")]
    [UpdatesUrl("https://www.codestack.net/labs/solidworks/my-toolbar/version-info.json")]
    public class MyToolbarSwAddin : ISwAddin
    {
        private ISldWorks m_App;
        private ICommandManager m_CmdMgr;
        private int m_AddinCookie;

        private CustomToolbarInfo m_ToolbarInfo;

        private LaunchKitController m_LaunchKit;

        #region SolidWorks Registration
        
        [ComRegisterFunction]
        public static void RegisterFunction(Type t)
        {
            try
            {
                var att = t.GetCustomAttributes(false).OfType<SwAddinAttribute>().FirstOrDefault();

                if (att == null)
                {
                    throw new NullReferenceException($"{typeof(SwAddinAttribute).FullName} is not set on {t.GetType().FullName}");
                }

                Microsoft.Win32.RegistryKey hklm = Microsoft.Win32.Registry.LocalMachine;
                Microsoft.Win32.RegistryKey hkcu = Microsoft.Win32.Registry.CurrentUser;

                string keyname = "SOFTWARE\\SolidWorks\\Addins\\{" + t.GUID.ToString() + "}";
                Microsoft.Win32.RegistryKey addinkey = hklm.CreateSubKey(keyname);
                addinkey.SetValue(null, 0);

                addinkey.SetValue("Description", att.Description);
                addinkey.SetValue("Title", att.Title);

                keyname = "Software\\SolidWorks\\AddInsStartup\\{" + t.GUID.ToString() + "}";
                addinkey = hkcu.CreateSubKey(keyname);
                addinkey.SetValue(null, Convert.ToInt32(att.LoadAtStartup), Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error while registering the addin: " + ex.Message);
            }
        }

        [ComUnregisterFunction]
        public static void UnregisterFunction(Type t)
        {
            try
            {
                Microsoft.Win32.RegistryKey hklm = Microsoft.Win32.Registry.LocalMachine;
                Microsoft.Win32.RegistryKey hkcu = Microsoft.Win32.Registry.CurrentUser;

                string keyname = "SOFTWARE\\SolidWorks\\Addins\\{" + t.GUID.ToString() + "}";
                hklm.DeleteSubKey(keyname);

                keyname = "Software\\SolidWorks\\AddInsStartup\\{" + t.GUID.ToString() + "}";
                hkcu.DeleteSubKey(keyname);
            }
            catch (Exception e)
            {
                Console.WriteLine("Error while unregistering the addin: " + e.Message);
            }
        }

        #endregion

        #region ISwAddin Implementation

        public MyToolbarSwAddin()
        {
        }

        public bool ConnectToSW(object ThisSW, int cookie)
        {
            try
            {
                m_App = (ISldWorks)ThisSW;

                m_LaunchKit = new LaunchKitController(this.GetType(),
                    new IntPtr(m_App.IFrameObject().GetHWnd()));

                m_LaunchKit.StartServices();

                m_AddinCookie = cookie;

                m_App.SetAddinCallbackInfo(0, this, m_AddinCookie);

                m_ToolbarInfo = GetToolbarInfo();

                m_CmdMgr = m_App.GetCommandManager(m_AddinCookie);

                AddCommandMgr();

                return true;
            }
            catch(Exception ex)
            {
                m_App.SendMsgToUser2("Critical Error:"
                    + System.Environment.NewLine
                    + ex.Message, 
                    (int)swMessageBoxIcon_e.swMbStop,
                    (int)swMessageBoxBtn_e.swMbOk);

                return false;
            }
        }

        public bool DisconnectFromSW()
        {
            try
            {
                if (m_ToolbarInfo?.Groups?.Any() == true)
                {
                    foreach (var grp in m_ToolbarInfo.Groups)
                    {
                        m_CmdMgr.RemoveCommandGroup(grp.Id);
                    }
                }
            }
            catch
            {
            }
            finally
            {
                Marshal.ReleaseComObject(m_CmdMgr);
                m_CmdMgr = null;
                Marshal.ReleaseComObject(m_App);
                m_App = null;

                GC.Collect();
                GC.WaitForPendingFinalizers();

                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            
            return true;
        }

        #endregion
        
        public void AddCommandMgr()
        {
            foreach(var grp in m_ToolbarInfo.Groups)
            {
                int err = 0;
                var cmdGroup = m_CmdMgr.CreateCommandGroup2(grp.Id, 
                    grp.Title, grp.Description, grp.Description, -1, false, ref err);

                var icons = grp.Icons;

                //NOTE: commands are not used but main icon will fail if toolbar commands image list is not specified

                if (SupportsHighResIcons)
                {
                    var iconsList = IconsConverter.ConvertIcons(icons, true);
                    cmdGroup.MainIconList = iconsList;
                    cmdGroup.IconList = iconsList;
                }
                else
                {
                    var bmpPath = IconsConverter.ConvertIcons(icons, false);

                    var smallIcon = bmpPath[0];
                    var largeIcon = bmpPath[1];

                    cmdGroup.SmallMainIcon = smallIcon;
                    cmdGroup.LargeMainIcon = largeIcon;

                    cmdGroup.SmallIconList = smallIcon;
                    cmdGroup.LargeIconList = largeIcon;
                }
                
                cmdGroup.HasToolbar = true;
                cmdGroup.HasMenu = false;
                cmdGroup.Activate();
            }
        }

        private bool SupportsHighResIcons
        {
            get
            {
                const int SW_2016_REV = 24;

                var majorRev = int.Parse(m_App.RevisionNumber().Split('.')[0]);

                return majorRev >= SW_2016_REV;
            }
        }

        private CustomToolbarInfo GetToolbarInfo()
        {
            var info = TryReadInfoFromCache();

            if (info == null)
            {
                info = new CustomToolbarInfo()
                {
                    Groups = new CommandGroupInfo[]
                    {
                        new CommandGroupInfo()
                        {
                            Id = 0,
                            Title = "CodeStack Toolbar",
                            Description = "Customized commands library toolbar",
                            Icons = new MasterIcons()
                            {
                                IconPath = TryCreateDefaultIcon()
                            }
                        }
                    }
                };

                TrySaveInfoToCache(info);
            }

            return info;
        }

        private static void TrySaveInfoToCache(CustomToolbarInfo info)
        {
            try
            {
                var dataFile = Locations.DataFilePath;

                var dir = Path.GetDirectoryName(dataFile);

                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(dataFile,
                    JsonConvert.SerializeObject(info, Formatting.Indented));
            }
            catch
            {
            }
        }

        private static CustomToolbarInfo TryReadInfoFromCache()
        {
            CustomToolbarInfo info = null;

            try
            {
                var dataFile = Locations.DataFilePath;
                
                if (File.Exists(dataFile))
                {
                    info = JsonConvert.DeserializeObject<CustomToolbarInfo>(
                        File.ReadAllText(dataFile), new IconListJsonConverter());
                }
            }
            catch
            {
            }
            
            return info;
        }

        private static string TryCreateDefaultIcon()
        {
            var imgPath = Path.Combine(Locations.AppDirectoryPath,
                    nameof(Resources.codestack_toolbar) + ".png");

            try
            {
                var dir = Path.GetDirectoryName(imgPath);

                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                Resources.codestack_toolbar.Save(imgPath);
            }
            catch
            {
            }

            return imgPath;
        }
    }
}