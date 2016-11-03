﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using Mono.Options;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace MoveToDesktop
{
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App : Application
	{
		private Window mainWindow;
		private Mutex mutex;
		private NotifyIcon notifyIcon;


		private class Command
		{
			public enum Type
			{
				NormalStartup,
				ShowUi,
				InstallTask,
				RemoveTask,
				CreateDesktop,
				SwitchDesktop,
				ListDesktops,
				RemoveDesktop,
				RemoveEmptyDesktops,
				MoveToDesktop,
				AppParameter,
				HwndParameter,
				ShowHelp,
			}

			public string Argument { get; private set; }

			public Type Id { get; private set; }

			public Command(Type id, string argument = null)
			{
				Id = id;
				Argument = argument;
			}
		}

		protected override void OnStartup(StartupEventArgs eventArgs)
		{
			base.OnStartup(eventArgs);
//#if DEBUG
			//Debugger.Launch();
//#endif
			List<Command> commands = new List<Command>();
			var p = new OptionSet()
			{
				$"Usage: {Assembly.GetExecutingAssembly().GetName().Name} [command]",
				"",
				"Commands:",
				{
					"install-task", "Install the scheduled task", v =>
					{
						if (v != null)
						{
							commands.Add(new Command(Command.Type.InstallTask));
						}
					}
				},
				{
					"remove-task", "Remove the scheduled task", v =>
					{
						if (v != null)
						{
							commands.Add(new Command(Command.Type.RemoveTask));
						}
					}
				},
				{
					"show-ui", "Show UI", v =>
					{
						if (v != null)
						{
							commands.Add(new Command(Command.Type.ShowUi));
						}
					}
				},
				{
					"create-desktop", "Create a new desktop", v =>
					{
						if (v != null)
						{
							commands.Add(new Command(Command.Type.CreateDesktop));
						}
					}
				},
				{
					"switch-desktop=", "switch to a desktop", v =>
					{
						if (v != null)
						{
							commands.Add(new Command(Command.Type.SwitchDesktop, v));
						}
					}
				},
				{
					"list-desktops", "list all desktops", v =>
					{
						if (v != null)
						{
							commands.Add(new Command(Command.Type.ListDesktops));
						}
					}
				},
				{
					"remove-desktop=", "remove desktop", v =>
					{
						if (v != null)
						{
							commands.Add(new Command(Command.Type.RemoveDesktop, v));
						}
					}
				},
				{
					"remove-empty-desktops", "remove empty desktops", v =>
					{
						if (v != null)
						{
							commands.Add(new Command(Command.Type.RemoveEmptyDesktops));
						}
					}
				},
				{
					"move-to-desktop=", "move hwnd or app to desktop", v =>
					{
						if (v != null)
						{
							commands.Add(new Command(Command.Type.MoveToDesktop, v));
						}
					}
				},
				{
					"app=", "app parameter", v =>
					{
						if (v != null)
						{
							commands.Add(new Command(Command.Type.AppParameter, v));
						}
					}
				},
				{
					"hwnd=", "hwnd parameter", v =>
					{
						if (v != null)
						{
							commands.Add(new Command(Command.Type.HwndParameter, v));
						}
					}
				},
				{
					"h|help", "show this message and exit", v =>
					{
						if (v != null)
						{
							commands.Add(new Command(Command.Type.ShowHelp));
						}
					}
				}
			};

			List<string> extra;
			try
			{
				extra = p.Parse(Environment.GetCommandLineArgs());
			}
			catch
			{
			}

			if (commands.Any(x=> x.Id == Command.Type.ShowHelp))
			{
				p.WriteOptionDescriptions(Console.Out);
				Application.Current.Shutdown();
				return;
			}


			if (commands.Any(x => x.Id == Command.Type.InstallTask || x.Id == Command.Type.RemoveTask))
			{
				// are we administrator
				if (!MainViewModel.IsAdministrator)
				{
					Console.Out.WriteLine("This command must be run as administrator");
					Application.Current.Shutdown();
					return;

				}
			}
			if (commands.Any(x => x.Id == Command.Type.InstallTask || x.Id == Command.Type.RemoveTask || x.Id == Command.Type.ShowUi))
			{
				// wait until the mutex is free
				do
				{
					Thread.Sleep(100);
					mutex = new Mutex(false, Settings.GuiMutex);
					if (mutex.WaitOne(0, false))
					{
						break;
					}
				} while (_contentLoaded);
			}
			else
			{
				mutex = new Mutex(false, Settings.GuiMutex);
				if (!mutex.WaitOne(0, false))
				{
					using (var wh = new EventWaitHandle(false, EventResetMode.AutoReset, "MoveToDesktopShow"))
					{
						wh.Set();
					}

					Application.Current.Shutdown();
					return;
				}
			}



			try
			{
				RunHelper.Start();
			}
			catch (Exception e)
			{
				MessageBox.Show($"Could not extract runner!\n\n{e.Message}", "MoveToDesktop", MessageBoxButton.OK, MessageBoxImage.Error);
				Application.Current.Shutdown();
				return;
			}

			if (commands.Count == 0)
			{
				commands.Add(new Command(Command.Type.NormalStartup));
			}

			RunCommands(commands);
		}


		private void SetupGui()
		{
			if (mainWindow != null)
				return;
			mainWindow = new MainWindow();

			new Task(() =>
			{
				using (var wh = new EventWaitHandle(false, EventResetMode.AutoReset, "MoveToDesktopShow"))
				{
					while (wh.WaitOne())
					{
						Dispatcher.BeginInvoke(new Action(() => {
							ShowWindow();
						}));
					}
				}
			}).Start();

			mainWindow.StateChanged += (sender, args) =>
			{
				if (mainWindow.WindowState == WindowState.Minimized)
				{
					notifyIcon.Visible = !Settings.HideTray;
					mainWindow.Hide();
				}
			};

			mainWindow.Closed += (sender, args) =>
			{
				notifyIcon.Visible = false;
				Application.Current.Shutdown();
			};



			notifyIcon = new System.Windows.Forms.NotifyIcon();
			notifyIcon.Icon = MoveToDesktop.Properties.Resources.icon;
			notifyIcon.Text = "MoveToDesktop is running";
			notifyIcon.Click += (sender, args) =>
			{
				ShowWindow();
			};

			notifyIcon.Visible = !Settings.HideTray;
		}

		private void RunCommands(ICollection<Command> commands)
		{
			foreach (var command in commands)
			{
				switch (command.Id)
				{
					case Command.Type.InstallTask:
						MainViewModel.InstallTask();
						break;
					case Command.Type.RemoveTask:
						MainViewModel.RemoveTask();
						break;
					case Command.Type.ShowUi:
						SetupGui();
						mainWindow.Show();
						break;

					
					case Command.Type.NormalStartup:
					default:
						SetupGui();
						if (!Settings.FirstTime)
						{
							mainWindow.WindowStyle = WindowStyle.None;
							mainWindow.Visibility = Visibility.Hidden;
							mainWindow.ShowInTaskbar = false;
							mainWindow.Show();
							mainWindow.Hide();
						}
						else
						{
							mainWindow.Show();
							Settings.FirstTime = false;
						}
						break;
				}
			}
		}

		protected override void OnExit(ExitEventArgs e)
		{
			RunHelper.Exit();
			mutex.ReleaseMutex();
			base.OnExit(e);
		}


		private void ShowWindow()
		{
			mainWindow.WindowStyle = WindowStyle.SingleBorderWindow;
			mainWindow.Show();
			mainWindow.Activate();
			mainWindow.WindowState = WindowState.Normal;
			mainWindow.ShowInTaskbar = true;
			mainWindow.Visibility = Visibility.Visible;
			mainWindow.Focus();

		}
	}


}
