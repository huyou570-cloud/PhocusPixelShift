using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal class App : Form
{
	private TextBox home = new TextBox();

	private TextBox output = new TextBox();

	private TextBox log = new TextBox();

	private ListBox files = new ListBox();

	private Button start = new Button();

	private Button stop = new Button();

	private Button show = new Button();

	private Button add = new Button();

	private Button clear = new Button();

	private Button up = new Button();

	private Button down = new Button();

	private Label status = new Label();

	private Process worker;

	private string runFolder;

	private bool busy;

	private ProgressBar progress = new ProgressBar();

	[STAThread]
	private static void Main()
	{
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(false);
		Application.Run(new App());
	}

	private App()
	{
		Text = "Phocus 四/六张像素位移合并 v0.4";
		Font = new Font("Microsoft YaHei UI", 10f);
		base.ClientSize = new Size(950, 730);
		MinimumSize = new Size(820, 700);
		base.StartPosition = FormStartPosition.CenterScreen;
		BackColor = Color.FromArgb(247, 249, 252);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 10,
			Padding = new Padding(22)
		};
		base.Controls.Add(tableLayoutPanel);
		int[] array = new int[10] { 50, 48, 48, 30, 190, 42, 42, 32, 0, 28 };
		foreach (int num in array)
		{
			tableLayoutPanel.RowStyles.Add((num == 0) ? new RowStyle(SizeType.Percent, 100f) : new RowStyle(SizeType.Absolute, num));
		}
		tableLayoutPanel.Controls.Add(new Label
		{
			Text = "四 / 六张原片 → Phocus 原生合并 → 16 位 TIFF",
			Font = new Font(Font.FontFamily, 16f, FontStyle.Bold),
			Dock = DockStyle.Fill,
			ForeColor = Color.FromArgb(22, 42, 65)
		}, 0, 0);
		home.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Hasselblad", "Phocus 4.1.1");
		output.Text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "合并结果");
		tableLayoutPanel.Controls.Add(PathRow("Phocus 目录", home), 0, 1);
		tableLayoutPanel.Controls.Add(PathRow("结果文件夹", output), 0, 2);
		tableLayoutPanel.Controls.Add(new Label
		{
			Text = "选择同组四张或六张 3FR，按拍摄顺序排列；程序自动识别模式。原片只读。",
			Dock = DockStyle.Fill
		}, 0, 3);
		files.Dock = DockStyle.Fill;
		files.HorizontalScrollbar = true;
		files.IntegralHeight = false;
		tableLayoutPanel.Controls.Add(files, 0, 4);
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill
		};
		Setup(add, "选择四/六张原片", delegate
		{
			using (OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Filter = "Hasselblad RAW (*.3fr)|*.3fr",
				Multiselect = true,
				Title = "选择同一组的四张或六张原片"
			})
			{
				if (openFileDialog.ShowDialog() == DialogResult.OK)
				{
					files.Items.Clear();
					{
						foreach (string item in openFileDialog.FileNames.OrderBy((string x) => x, StringComparer.OrdinalIgnoreCase))
						{
							files.Items.Add(item);
						}
						return;
					}
				}
			}
		});
		Setup(clear, "清空", delegate
		{
			files.Items.Clear();
		});
		Setup(up, "上移", delegate
		{
			MoveFile(-1);
		});
		Setup(down, "下移", delegate
		{
			MoveFile(1);
		});
		flowLayoutPanel.Controls.AddRange(new Control[4] { add, clear, up, down });
		tableLayoutPanel.Controls.Add(flowLayoutPanel, 0, 5);
		FlowLayoutPanel flowLayoutPanel2 = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill
		};
		Setup(start, "合并并导出 TIFF", Start);
		start.Width = 180;
		start.BackColor = Color.FromArgb(27, 92, 173);
		start.ForeColor = Color.White;
		start.FlatStyle = FlatStyle.Flat;
		Setup(stop, "停止", delegate
		{
			if (busy)
			{
				Directory.CreateDirectory(Path.Combine(runFolder, ".work"));
				File.WriteAllText(Path.Combine(runFolder, ".work", "cancel.request"), "cancel");
				status.Text = "正在停止，请稍候……";
				stop.Enabled = false;
			}
		});
		Setup(show, "打开结果文件夹", delegate
		{
			if (Directory.Exists(runFolder))
			{
				Process.Start(new ProcessStartInfo("explorer.exe", "\"" + runFolder + "\"")
				{
					UseShellExecute = true
				});
			}
		});
		stop.Enabled = false;
		show.Enabled = false;
		flowLayoutPanel2.Controls.AddRange(new Control[3] { start, stop, show });
		tableLayoutPanel.Controls.Add(flowLayoutPanel2, 0, 6);
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2
		};
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
		status.Dock = DockStyle.Fill;
		status.Text = "就绪 · 自动识别四张或六张模式";
		progress.Dock = DockStyle.Fill;
		progress.Height = 20;
		tableLayoutPanel2.Controls.Add(status, 0, 0);
		tableLayoutPanel2.Controls.Add(progress, 1, 0);
		tableLayoutPanel.Controls.Add(tableLayoutPanel2, 0, 7);
		log.Multiline = true;
		log.ScrollBars = ScrollBars.Vertical;
		log.ReadOnly = true;
		log.Dock = DockStyle.Fill;
		log.BackColor = Color.White;
		tableLayoutPanel.Controls.Add(log, 0, 8);
		tableLayoutPanel.Controls.Add(new Label
		{
			Text = "四张模式保持原生尺寸；六张模式输出更高尺寸。均导出 16 位 TIFF，不支持 16 张。",
			Dock = DockStyle.Fill,
			ForeColor = Color.DimGray
		}, 0, 9);
		base.FormClosing += delegate(object s, FormClosingEventArgs e)
		{
			if (busy)
			{
				e.Cancel = true;
				Append("任务尚未结束，请先点击“停止”。");
			}
		};
	}

	private Control PathRow(string title, TextBox box)
	{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
		tableLayoutPanel.Dock = DockStyle.Fill;
		tableLayoutPanel.ColumnCount = 3;
		TableLayoutPanel tableLayoutPanel2 = tableLayoutPanel;
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90f));
		tableLayoutPanel2.Controls.Add(new Label
		{
			Text = title,
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft
		}, 0, 0);
		box.Dock = DockStyle.Fill;
		tableLayoutPanel2.Controls.Add(box, 1, 0);
		Button button = new Button();
		button.Text = "浏览…";
		button.Dock = DockStyle.Fill;
		Button button2 = button;
		button2.Click += delegate
		{
			using (FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog
			{
				SelectedPath = (Directory.Exists(box.Text) ? box.Text : "")
			})
			{
				if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
				{
					box.Text = folderBrowserDialog.SelectedPath;
				}
			}
		};
		tableLayoutPanel2.Controls.Add(button2, 2, 0);
		return tableLayoutPanel2;
	}

	private void Setup(Button b, string title, EventHandler click)
	{
		b.Text = title;
		b.Width = 130;
		b.Height = 34;
		b.Click += click;
	}

	private void MoveFile(int delta)
	{
		int selectedIndex = files.SelectedIndex;
		int num = selectedIndex + delta;
		if (selectedIndex >= 0 && num >= 0 && num < files.Items.Count)
		{
			object item = files.Items[selectedIndex];
			files.Items.RemoveAt(selectedIndex);
			files.Items.Insert(num, item);
			files.SelectedIndex = num;
		}
	}

	private void Append(string s)
	{
		if (base.IsDisposed)
		{
			return;
		}
		if (base.InvokeRequired)
		{
			BeginInvoke(new Action<string>(Append), s);
			return;
		}
		log.AppendText(s + Environment.NewLine);
		if (s.StartsWith("1/4"))
		{
			status.Text = "正在准备原生模板……";
		}
		if (s.StartsWith("2/4"))
		{
			status.Text = "正在处理原片……";
		}
		if (s.StartsWith("3/4"))
		{
			status.Text = "正在整理合并数据……";
		}
		if (s.StartsWith("4/4"))
		{
			status.Text = "正在显色并导出 TIFF……";
		}
		if (s.StartsWith("显色导出进度"))
		{
			status.Text = s;
		}
	}

	private void SetBusy(bool v)
	{
		busy = v;
		start.Enabled = !v;
		stop.Enabled = v;
		Button button = add;
		Button button2 = clear;
		Button button3 = up;
		Button button4 = down;
		ListBox listBox = files;
		TextBox textBox = home;
		bool flag = (output.Enabled = !v);
		flag = (textBox.Enabled = flag);
		flag = (listBox.Enabled = flag);
		flag = (button4.Enabled = flag);
		flag = (button3.Enabled = flag);
		flag = (button2.Enabled = flag);
		button.Enabled = flag;
		progress.Style = (v ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks);
	}

	private void Start(object sender, EventArgs e)
	{
		if (files.Items.Count != 4 && files.Items.Count != 6)
		{
			MessageBox.Show(this, "请选择恰好四张或六张同组 3FR 原片。", "原片数量");
			return;
		}
		if (!File.Exists(Path.Combine(home.Text, "PhocusApi64.dll")) || !File.Exists(Path.Combine(home.Text, "Phocus.dll")))
		{
			MessageBox.Show(this, "请选择安装好的 Phocus 4.1.1 文件夹。", "Phocus 目录");
			return;
		}
		try
		{
			Directory.CreateDirectory(output.Text);
			runFolder = Path.Combine(Path.GetFullPath(output.Text), "像素位移-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
			Directory.CreateDirectory(runFolder);
			string text = Path.Combine(runFolder, "任务.json");
			File.WriteAllText(text, new JavaScriptSerializer().Serialize(new
			{
				Home = home.Text,
				Inputs = files.Items.Cast<string>().ToArray(),
				Folder = runFolder
			}), new UTF8Encoding(false));
			string text2 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PixelShiftWorker.exe");
			if (!File.Exists(text2))
			{
				throw new Exception("缺少 PixelShiftWorker.exe，请完整解压软件包。");
			}
			log.Clear();
			show.Enabled = false;
			SetBusy(true);
			status.Text = "正在启动……";
			worker = new Process
			{
				StartInfo = new ProcessStartInfo(text2, "--run \"" + text + "\"")
				{
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					StandardOutputEncoding = Encoding.UTF8,
					StandardErrorEncoding = Encoding.UTF8
				},
				EnableRaisingEvents = true
			};
			worker.OutputDataReceived += delegate(object o, DataReceivedEventArgs a)
			{
				if (a.Data != null)
				{
					Append(a.Data);
				}
			};
			worker.ErrorDataReceived += delegate(object o, DataReceivedEventArgs a)
			{
				if (a.Data != null)
				{
					Append(a.Data);
				}
			};
			worker.Exited += delegate
			{
				worker.WaitForExit();
				int code = worker.ExitCode;
				if (!base.IsDisposed)
				{
					BeginInvoke((Action)delegate
					{
						SetBusy(false);
						show.Enabled = true;
						bool flag = code == 0 && File.Exists(Path.Combine(runFolder, "验证记录.json"));
						status.Text = (flag ? "完成 · 16 位 TIFF 已生成并验证" : (File.Exists(Path.Combine(runFolder, ".work", "cancel.request")) ? "已停止 · 原片未改动" : "处理失败 · 请保留日志"));
						progress.Value = (flag ? 100 : 0);
					});
				}
			};
			worker.Start();
			worker.BeginOutputReadLine();
			worker.BeginErrorReadLine();
		}
		catch (Exception ex)
		{
			SetBusy(false);
			MessageBox.Show(this, ex.Message, "无法启动");
		}
	}
}
