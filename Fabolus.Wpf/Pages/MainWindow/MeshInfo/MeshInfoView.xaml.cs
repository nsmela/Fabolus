﻿using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Fabolus.Wpf.Pages.MainWindow.MeshInfo;
/// <summary>
/// Interaction logic for MeshInfoView.xaml
/// </summary>
public partial class MeshInfoView : UserControl {
    public MeshInfoView() {
        DataContext = new MeshInfoViewModel();
        WeakReferenceMessenger.Default.Register<MeshInfoView, MeshInfoRequestMessage>(this, (r, m) => m.Reply(r.view));
        InitializeComponent();
    }
}
