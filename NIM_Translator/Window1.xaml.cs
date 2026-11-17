using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using NIMEngine;
using NIMEngine.Platform;
using NIMEngine.Platform.Nodes;

namespace NIM
{
    public partial class Window1 : Window
    {
        private DispatcherTimer _testTimer;

        public Window1()
        {
            InitializeComponent();
            Loaded += Window1_Loaded;
            Closed += (s, e) => { if (_testTimer != null) _testTimer.Stop(); };
        }

        private void Window1_Loaded(object sender, RoutedEventArgs e)
        {
            LoadTestData();
        }

        private void LoadTestData()
        {
            var list = new List<PlatformConfig>();
            var rnd = new Random();

            string[] cloud = { "ChatGpt", "Gemini", "DeepSeek", "Claude", "Grok", "Qwen", "Kimi", "Doubao", "GLM", "Mistral", "Llama-API", "Hunyuan" };
            foreach (var n in cloud)
                list.Add(new PlatformConfig { Name = n, Type = P_Node.CloudAI, Enable = rnd.Next(2) == 0, Model = n.ToLower() + "-latest" });

            string[] local = { "LM Studio", "Ollama", "KoboldCpp", "llama.cpp Server", "vLLM" };
            foreach (var n in local)
                list.Add(new PlatformConfig { Name = n, Type = P_Node.LocalAI, Enable = rnd.Next(2) == 0, Model = "qwen2.5-14b-instruct", LocalPort = 1234 });

            string[] trad = { "DeepL", "Google Translate", "Bing Translator", "Baidu", "Youdao", "Caiyun" };
            foreach (var n in trad)
                list.Add(new PlatformConfig { Name = n, Type = P_Node.Traditional, Enable = rnd.Next(2) == 0 });

            list.Add(new PlatformConfig { Name = "HumanTranslation", Type = P_Node.Interactive, Enable = true });
            list.Add(new PlatformConfig { Name = "ManualReview", Type = P_Node.Interactive, Enable = false });

            NodeViewCtrl.SetNodes(list);

            NodeViewCtrl.NodeEnableChanged += (s, ev) =>
                System.Diagnostics.Debug.WriteLine(ev.Config.Name + " -> " + ev.Enable);
            NodeViewCtrl.NodeClicked += (s, ev) =>
                System.Diagnostics.Debug.WriteLine("Clicked: " + ev.Config.Name);


            // Simulate random node calls to test the LEDs
            _testTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _testTimer.Tick += (s, ev) =>
            {
                var cfg = list[rnd.Next(list.Count)];
                NodeViewCtrl.BlinkNode(cfg.Name, rnd.Next(150, 1200));
            };
            _testTimer.Start();
        }
    }
}