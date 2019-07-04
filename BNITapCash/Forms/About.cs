﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BNITapCash.Forms
{
    public partial class About : Form
    {
        public About()
        {
            InitializeComponent();
            InitData();
        }

        private void InitData()
        {
            txtVersion.Text = Properties.Resources.VersionApp;
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            linkLabel1.LinkVisited = true;
            System.Diagnostics.Process.Start(Properties.Resources.DeveloperURL);
        }

        private void label2_Click(object sender, EventArgs e)
        {
            Dispose();
        }

        private void txtVersion_Click(object sender, EventArgs e)
        {

        }

        private void About_Load(object sender, EventArgs e)
        {

        }
    }
}