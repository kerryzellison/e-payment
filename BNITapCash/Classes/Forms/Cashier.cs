using BNITapCash.API;
using BNITapCash.API.request;
using BNITapCash.API.response;
using BNITapCash.Bank.BNI;
using BNITapCash.Bank.DataModel;
using BNITapCash.Card.Mifare;
using BNITapCash.Classes.Forms;
using BNITapCash.Classes.Helper;
using BNITapCash.ConstantVariable;
using BNITapCash.DB;
using BNITapCash.Forms;
using BNITapCash.Helper;
using BNITapCash.Interface;
using BNITapCash.Miscellaneous.Webcam;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace BNITapCash
{
    public partial class Cashier : Form, EventFormHandler
    {
        private Login home;
        private BNI bni;
        private DBConnect database;
        public PictureBox webcamImage;
        private Webcam camera;
        private MifareCard mifareCard;
        private RESTAPI restApi;
        private AutoCompleteStringCollection autoComplete;
        private string ip_address_server;
        private ParkingIn parkingIn;

        public string UIDCard
        {
            get
            {
                return textBox1.Text;
            }

            set
            {
                textBox1.Text = value;
            }
        }

        public Cashier(Login home)
        {
            InitializeComponent();
            this.home = home;
            Initialize();
        }

        private void Initialize()
        {
            this.webcamImage = webcam;
            if (Properties.Settings.Default.WebcamEnabled)
            {
                this.camera = new Webcam(this);
            }
            this.restApi = new RESTAPI();
            this.database = new DBConnect();
            this.parkingIn = new ParkingIn();
            autoComplete = new AutoCompleteStringCollection();
            nonCash.Checked = true;
            ip_address_server = Properties.Settings.Default.IPAddressServer;

            StartLiveCamera();

            this.bni = new BNI();

            this.mifareCard = new MifareCard(this);
            this.mifareCard.RunMain();

            // initialize vehicle type options            
            try
            {
                comboBox1.Items.Add("- Pilih Tipe Kendaraan -");
                string masterDataFile = TKHelper.GetApplicationExecutableDirectoryName() + Constant.PATH_FILE_MASTER_DATA_PARKING_OUT;
                using (StreamReader reader = new StreamReader(masterDataFile))
                {
                    string json = reader.ReadToEnd();
                    dynamic vehicleTypes = JsonConvert.DeserializeObject(json);
                    foreach (var types in vehicleTypes["VehicleTypes"])
                    {
                        comboBox1.Items.Add(types);
                    }
                    comboBox1.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                notifyIcon.ShowBalloonTip(Constant.NOTIFICATION_TRAY_TIMEOUT, "Error", Constant.ERROR_MESSAGE_FAIL_TO_FETCH_VEHICLE_TYPE_DATA, ToolTipIcon.Error);
            }
        }

        private void StartLiveCamera()
        {
            CameraHelper.StartIpCamera(LiveCamera);
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {

        }

        private void Cashier_Load(object sender, EventArgs e)
        {

        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            string feedback = this.ValidateFields();
            if (feedback == Constant.MESSAGE_OK)
            {
                // check the payment method whether it's cash or non-cash
                int totalFare = TKHelper.IDRToNominal(txtGrandTotal.Text.ToString());
                string paymentMethod = nonCash.Checked ? "NCSH" : "CASH";

                if (paymentMethod == "NCSH")
                {
                    string bankCode = "BNI";
                    string ipv4 = TKHelper.GetLocalIPAddress();
                    string TIDSettlement = Properties.Settings.Default.TID;
                    string operator_name = Properties.Settings.Default.Username;

                    // need to disconnect SCard from WinsCard.dll beforehand in order to execute further actions to avoid 'Outstanding Connection' Exception.
                    mifareCard.disconnect();

                    DataDeduct responseDeduct = bni.DeductBalance(bankCode, ipv4, TIDSettlement, operator_name);
                    if (!responseDeduct.IsError)
                    {
                        string base64WebcamImage = CameraHelper.CaptureWebcamImage(camera, webcamImage);
                        string base64LiveCameraSnapshot = CameraHelper.SnapshotLiveCamera();
                        if (!string.IsNullOrEmpty(base64LiveCameraSnapshot))
                        {
                            ParkingOut parkingOut = SendDataToServer(totalFare, base64WebcamImage, base64LiveCameraSnapshot, paymentMethod);
                            StoreDataToDatabase(responseDeduct, parkingOut);
                            notifyIcon.ShowBalloonTip(Constant.NOTIFICATION_TRAY_TIMEOUT, "Success", Constant.TRANSACTION_SUCCESS, ToolTipIcon.Info);
                            Clear(true);
                        }
                    }
                    else
                    {
                        notifyIcon.ShowBalloonTip(Constant.NOTIFICATION_TRAY_TIMEOUT, "Error", responseDeduct.Message, ToolTipIcon.Error);
                    }
                }
                else
                {
                    string base64WebcamImage = CameraHelper.CaptureWebcamImage(camera, webcamImage);
                    string base4LiveCameraSnapshot = CameraHelper.SnapshotLiveCamera();
                    if (!string.IsNullOrEmpty(base4LiveCameraSnapshot))
                    {
                        ParkingOut parkingOut = SendDataToServer(totalFare, base64WebcamImage, base4LiveCameraSnapshot, paymentMethod);
                        notifyIcon.ShowBalloonTip(Constant.NOTIFICATION_TRAY_TIMEOUT, "Success", Constant.TRANSACTION_SUCCESS, ToolTipIcon.Info);
                        Clear(true);
                    }
                }
            }
            else
            {
                notifyIcon.ShowBalloonTip(Constant.NOTIFICATION_TRAY_TIMEOUT, "Warning", feedback, ToolTipIcon.Warning);
            }
        }

        private void StoreDataToDatabase(DataDeduct dataDeduct, ParkingOut parkingOut)
        {
            try
            {
                // store deduct result card to server
                string result = dataDeduct.DeductResult;
                int amount = dataDeduct.Amount;
                string created = dataDeduct.CreatedDatetime;
                string bank = dataDeduct.Bank;
                string ipv4 = dataDeduct.IpAddress;
                string operatorName = dataDeduct.OperatorName;
                string idReader = dataDeduct.IdReader;
                int parkingOutId = parkingOut.ParkingOutId;

                string query = "INSERT INTO deduct_card_results (parking_out_id, result, amount, transaction_dt, bank, ipv4, operator, ID_reader, created) VALUES('" + parkingOutId + "', '" +
                    result + "', '" + amount + "', '" + created + "', '" + bank + "', '" + ipv4 + "', '" + operatorName + "', '" + idReader + "', '" + created + "')";

                database.Insert(query);
            }
            catch (Exception ex)
            {
                notifyIcon.ShowBalloonTip(Constant.NOTIFICATION_TRAY_TIMEOUT, "Error", ex.Message, ToolTipIcon.Error);
                return;
            }
        }

        private ParkingOut SendDataToServer(int totalFare, string base64Image, string base64LiveCameraImage, string paymentMethod, string bankCode = "")
        {
            string uid = textBox1.Text.ToString();
            string uidType = TKHelper.GetUidType(uid);
            string vehicle = comboBox1.Text.ToString();
            string datetimeOut = TKHelper.ConvertDatetimeToDefaultFormat(textBox4.Text.ToString());
            string username = Properties.Settings.Default.Username;
            string plateNumber = textBox2.Text.ToString();
            string ipAddressLocal = TKHelper.GetLocalIPAddress();
            ParkingOutRequest parkingOutRequest = new ParkingOutRequest(uidType, uid, vehicle, datetimeOut, username, plateNumber, totalFare, ipAddressLocal, paymentMethod, bankCode, base64Image, base64LiveCameraImage);
            var sent_param = JsonConvert.SerializeObject(parkingOutRequest);

            DataResponseObject response = (DataResponseObject)restApi.post(ip_address_server, Properties.Resources.SaveDataParkingAPIURL, true, sent_param);
            if (response != null)
            {
                switch (response.Status)
                {
                    case 206:
                        return JsonConvert.DeserializeObject<ParkingOut>(response.Data.ToString());
                    default:
                        notifyIcon.ShowBalloonTip(Constant.NOTIFICATION_TRAY_TIMEOUT, "Warning", response.Message, ToolTipIcon.Warning);
                        return null;
                }
            }
            else
            {
                notifyIcon.ShowBalloonTip(Constant.NOTIFICATION_TRAY_TIMEOUT, "Warning", Constant.ERROR_MESSAGE_INVALID_RESPONSE_FROM_SERVER, ToolTipIcon.Warning);
                return null;
            }
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            Clear(true);
        }

        public void Clear(bool include_uid = false)
        {
            if (include_uid)
                textBox1.Text = "Barcode/UID Kartu";
            textBox2.Text = "Nomor Plat Kendaraan";
            textBox3.Text = "- - -  00:00:00";
            textBox4.Text = TKHelper.GetCurrentDatetime();
            txtHour.Text = "";
            txtMinute.Text = "";
            txtSecond.Text = "";
            txtGrandTotal.Text = "0";
            this.ResetComboBox();

            PictFace.Image = Properties.Resources.no_image;
            PictFace.SizeMode = PictureBoxSizeMode.StretchImage;
            PictVehicle.Image = Properties.Resources.no_image;
            PictVehicle.SizeMode = PictureBoxSizeMode.StretchImage;

            nonCash.Checked = true;
        }

        private void textBox1_Click(object sender, EventArgs e)
        {
            if (textBox1.Text.ToLower() == "barcode/uid kartu")
                this.TextListener("barcode/uid kartu");
        }

        private void TextListener(string field, bool is_textchanged = false)
        {
            if (!is_textchanged)
            {
                if (field == "nomor plat kendaraan")
                {
                    textBox2.Clear();
                }
                if (field == "barcode/uid kartu")
                {
                    textBox1.Clear();
                }
            }
            textBox1.ForeColor = Color.FromArgb(78, 184, 206);
            textBox2.ForeColor = Color.FromArgb(78, 184, 206);
        }

        private void logout_Click(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show(Constant.CONFIRMATION_MESSAGE_BEFORE_EXIT, "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result == DialogResult.Yes)
            {
                Properties.Settings.Default.Password = "";
                Properties.Settings.Default.RememberMe = "no";
                Properties.Settings.Default.Save();

                // redirect to sign-in form
                Hide();
                this.home.Clear();
                this.home.Show();
            }
        }

        private void logout_MouseHover(object sender, EventArgs e)
        {
            toolTip1.SetToolTip(logout, "Logout");
        }

        private void panel10_Paint(object sender, PaintEventArgs e)
        {

        }

        private void textBox2_TextChanged(object sender, EventArgs e)
        {
            this.TextListener("Nomor Plat Kendaraan", true);
            textBox2.CharacterCasing = CharacterCasing.Upper;
        }

        private void textBox2_Click(object sender, EventArgs e)
        {
            if (textBox2.Text.ToLower() == "nomor plat kendaraan")
                this.TextListener("nomor plat kendaraan");
        }

        private string ValidateFields()
        {
            if (textBox2.Text.ToLower() == "nomor plat kendaraan" || textBox2.Text == "")
            {
                return Constant.WARMING_MESSAGE_PLATE_NUMBER_NOT_EMPTY;
            }

            if (textBox4.Text.ToLower() == "- - -  00:00:00" || textBox4.Text == "")
            {
                return Constant.WARNING_MESSAGE_DATETIME_LEAVE_NOT_EMPTY;
            }

            if (textBox1.Text.ToLower() == "barcode/uid kartu" || textBox1.Text == "")
            {
                return Constant.WARNING_MESSAGE_UID_CARD_NOT_EMPTY;
            }

            if (comboBox1.SelectedIndex == -1 || comboBox1.SelectedIndex == 0)
            {
                return Constant.WARNING_MESSAGE_VEHICLE_TYPE_NOT_EMPTY;
            }
            return Constant.MESSAGE_OK;
        }

        private void TimerTick(object sender, EventArgs e)
        {
            textBox4.Text = TKHelper.GetCurrentDatetime();
        }

        private void StartTimer()
        {
            System.Windows.Forms.Timer tmr = new System.Windows.Forms.Timer();
            tmr.Interval = 1000; // 1 second
            tmr.Tick += new EventHandler(TimerTick);
            tmr.Enabled = true;
        }

        private void ResetComboBox()
        {
            comboBox1.SelectedIndex = 0;
            comboBox1.ResetText();
            comboBox1.SelectedText = "- Pilih Tipe Kendaraan -";
        }

        private void comboBox1_SelectionChangeCommitted(object sender, EventArgs e)
        {
            if (comboBox1.SelectedIndex != 0)
            {
                if (textBox1.Text != "" && textBox1.Text != "Barcode/UID Kartu")
                {
                    // send data API
                    var APIUrl = Properties.Resources.RequestUIDFareAPIURL;

                    string uidType = TKHelper.GetUidType(UIDCard);
                    string vehicle = comboBox1.Text.ToString();
                    RequestFareRequest requestFare = new RequestFareRequest(uidType, UIDCard, vehicle);
                    var sent_param = JsonConvert.SerializeObject(requestFare);

                    DataResponseObject response = (DataResponseObject)restApi.post(ip_address_server, APIUrl, true, sent_param);
                    if (response != null)
                    {
                        switch (response.Status)
                        {
                            case 206:
                                parkingIn = JsonConvert.DeserializeObject<ParkingIn>(response.Data.ToString());

                                txtHour.Text = TKHelper.GetValueTime(parkingIn.ParkDuration, "hour");
                                txtMinute.Text = TKHelper.GetValueTime(parkingIn.ParkDuration, "minute");
                                txtSecond.Text = TKHelper.GetValueTime(parkingIn.ParkDuration, "second");

                                txtGrandTotal.Text = TKHelper.IDR(parkingIn.Fare.ToString());

                                string[] datetimeIn = parkingIn.DatetimeIn.Split(' ');
                                textBox3.Text = TKHelper.ConvertDatetime(datetimeIn[0], datetimeIn[1]);

                                string[] datetimeOut = parkingIn.DatetimeOut.Split(' ');
                                textBox4.Text = TKHelper.ConvertDatetime(datetimeOut[0], datetimeOut[1]);

                                // Load Picture of face and plate number
                                string faceImage = parkingIn.FaceImage;
                                if (string.IsNullOrEmpty(faceImage))
                                {
                                    PictFace.Image = Properties.Resources.no_image;
                                }
                                else
                                {
                                    try
                                    {
                                        string URL_pict_face = Constant.URL_PROTOCOL + Properties.Settings.Default.IPAddressServer + Properties.Resources.repo + "/" + faceImage;
                                        PictFace.Load(URL_pict_face);
                                    }
                                    catch (Exception)
                                    {
                                        PictFace.Image = Properties.Resources.no_image;
                                    }
                                }
                                PictFace.BackgroundImageLayout = ImageLayout.Stretch;
                                PictFace.SizeMode = PictureBoxSizeMode.StretchImage;

                                string plateNumberImage = parkingIn.PlateNumberImage;
                                if (string.IsNullOrEmpty(plateNumberImage))
                                {
                                    PictVehicle.Image = Properties.Resources.no_image;
                                }
                                else
                                {
                                    try
                                    {
                                        string URL_pict_vehicle = Constant.URL_PROTOCOL + Properties.Settings.Default.IPAddressServer + Properties.Resources.repo + "/" + parkingIn.PlateNumberImage;
                                        PictVehicle.Load(URL_pict_vehicle);
                                    }
                                    catch (Exception)
                                    {
                                        PictVehicle.Image = Properties.Resources.no_image;
                                    }
                                }
                                PictVehicle.BackgroundImageLayout = ImageLayout.Stretch;
                                PictVehicle.SizeMode = PictureBoxSizeMode.StretchImage;
                                break;
                            default:
                                notifyIcon.ShowBalloonTip(Constant.NOTIFICATION_TRAY_TIMEOUT, "Error", response.Message, ToolTipIcon.Error);
                                Clear();
                                break;
                        }
                    }
                    else
                    {
                        notifyIcon.ShowBalloonTip(Constant.NOTIFICATION_TRAY_TIMEOUT, "Error", Constant.ERROR_MESSAGE_FAIL_TO_CONNECT_SERVER, ToolTipIcon.Error);
                    }
                }
                else
                {
                    notifyIcon.ShowBalloonTip(Constant.NOTIFICATION_TRAY_TIMEOUT, "Error", Constant.WARNING_MESSAGE_UNTAPPED_CARD, ToolTipIcon.Error);
                    this.ResetComboBox();
                    return;
                }
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show(Constant.CONFIRMATION_MESSAGE_BEFORE_EXIT, "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result == DialogResult.Yes)
            {
                Dispose();
                System.Environment.Exit(1);
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            this.WindowState = FormWindowState.Minimized;
        }

        private void textBox4_TextChanged(object sender, EventArgs e)
        {

        }

        private AutoCompleteStringCollection SearchBarcode(string keyword)
        {
            var queryParam = "?barcode=" + keyword;
            var ApiURL = Properties.Resources.SearchBarcodeAPIURL + queryParam;
            DataResponseArray response = (DataResponseArray)restApi.get(ip_address_server, ApiURL, false);
            if (response != null)
            {
                if (response.Status == 206)
                {
                    string data = response.Data.ToString();
                    List<Barcode> barcodes = JsonConvert.DeserializeObject<List<Barcode>>(data);
                    if (barcodes.Count > 0)
                    {
                        foreach (Barcode barcode in barcodes)
                        {
                            listBarcodeSuggestion.Items.Add(barcode.barcode);
                            //autoComplete.Add(barcode.barcode);
                        }
                    }
                }
            }
            return autoComplete;
        }

        private void textBox1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                listBarcodeSuggestion.Visible = true;
                string barcode = textBox1.Text.ToString();
                listBarcodeSuggestion.Items.Clear();
                SearchBarcode(barcode);
                listBarcodeSuggestion.DroppedDown = true;
                listBarcodeSuggestion.Focus();
                if (listBarcodeSuggestion.Items.Count == 0)
                {
                    listBarcodeSuggestion.Items.Add("Data Tidak Ditemukan");
                    textBox1.Focus();

                }
                else if (listBarcodeSuggestion.Items.Count == 1)
                {
                    textBox1.AutoCompleteCustomSource = autoComplete;
                }
            }
        }

        private void selectBarcode(object sender, EventArgs e)
        {
            if (listBarcodeSuggestion.SelectedItem.ToString() != "Data Tidak Ditemukan")
            {
                textBox1.Text = listBarcodeSuggestion.Text;
                textBox1.Text = listBarcodeSuggestion.Text;
                listBarcodeSuggestion.Visible = false;
            }
        }

        private void button4_Click(object sender, EventArgs e)
        {
            ReprintTicket();
        }

        private void buttonGenerateReport_Click(object sender, EventArgs e)
        {
            PrintReportOperator();
        }

        private void ReprintTicket()
        {
            string reprintTicketApiUrl = Properties.Resources.ReprintTicketAPIURL;
            DataResponseObject response = (DataResponseObject)restApi.get(ip_address_server, reprintTicketApiUrl, true);
            if (response != null)
            {
                if (response.Status == 206)
                {
                    notifyIcon.ShowBalloonTip(Constant.NOTIFICATION_TRAY_TIMEOUT, "Success", Constant.REPRINT_TICKET_SUCCESS, ToolTipIcon.Info);
                }
                else
                {
                    notifyIcon.ShowBalloonTip(Constant.NOTIFICATION_TRAY_TIMEOUT, "Error", response.Message, ToolTipIcon.Error);
                }
            }
            else
            {
                notifyIcon.ShowBalloonTip(Constant.NOTIFICATION_TRAY_TIMEOUT, "Error", Constant.ERROR_MESSAGE_INVALID_RESPONSE_FROM_SERVER, ToolTipIcon.Error);
            }
        }

        private void PrintReportOperator()
        {
            string generateReportApiUrl = Properties.Resources.GenerateReportAPIURL;
            PrintReportRequest printReportRequest = new PrintReportRequest(Properties.Settings.Default.Username);
            var sentParam = JsonConvert.SerializeObject(printReportRequest);
            DataResponseObject response = (DataResponseObject)restApi.post(ip_address_server, generateReportApiUrl, true, sentParam);
            if (response != null)
            {
                switch (response.Status)
                {
                    case 206:
                        notifyIcon.ShowBalloonTip(Constant.NOTIFICATION_TRAY_TIMEOUT, "Success", Constant.PRINT_REPORT_OPERATOR_SUCCESS, ToolTipIcon.Info);
                        break;
                    case 208:
                        notifyIcon.ShowBalloonTip(Constant.NOTIFICATION_TRAY_TIMEOUT, "Success", response.Message, ToolTipIcon.Warning);
                        break;
                    default:
                        notifyIcon.ShowBalloonTip(Constant.NOTIFICATION_TRAY_TIMEOUT, "Success", response.Message, ToolTipIcon.Error);
                        break;
                }
            }
            else
            {
                notifyIcon.ShowBalloonTip(Constant.NOTIFICATION_TRAY_TIMEOUT, "Error", Constant.ERROR_MESSAGE_INVALID_RESPONSE_FROM_SERVER, ToolTipIcon.Error);
            }

        }

        private void buttonLostTicket_Click(object sender, EventArgs e)
        {
            mifareCard.Stop();
            database.DisposeDatabaseConnection();
            CameraHelper.StopIpCamera(LiveCamera);
            LostTicket lostTicket = new LostTicket(home);
            lostTicket.Show();
            Hide();
            Dispose();
            UnsubscribeEvents();
            TKHelper.ClearGarbage();
        }

        private void buttonFreePass_Click(object sender, EventArgs e)
        {
            CameraHelper.StopIpCamera(LiveCamera);
            mifareCard.Stop();
            database.DisposeDatabaseConnection();
            FreePass freePass = new FreePass(home);
            freePass.Show();
            Hide();
            Dispose();
            UnsubscribeEvents();
            TKHelper.ClearGarbage();
        }

        private void buttonPassKadeKeluar_Click(object sender, EventArgs e)
        {
            CameraHelper.StopIpCamera(LiveCamera);
            mifareCard.Stop();
            database.DisposeDatabaseConnection();
            PassKadeOut passKadeOut = new PassKadeOut(home);
            passKadeOut.Show();
            Hide();
            Dispose();
            UnsubscribeEvents();
            TKHelper.ClearGarbage();
        }

        public void UnsubscribeEvents()
        {
            textBox1.Click -= textBox1_Click;
            textBox1.KeyDown -= textBox1_KeyDown;
            textBox1.TextChanged -= textBox1_TextChanged;

            listBarcodeSuggestion.SelectedIndexChanged -= selectBarcode;

            comboBox1.SelectionChangeCommitted -= comboBox1_SelectionChangeCommitted;

            textBox2.Click -= textBox2_Click;
            textBox2.TextChanged -= textBox2_TextChanged;

            buttonGenerateReport.Click -= buttonGenerateReport_Click;
            buttonReprint.Click -= button4_Click;
            buttonLostTicket.Click -= buttonLostTicket_Click;
            buttonFreePass.Click -= buttonFreePass_Click;
            buttonPassKadeKeluar.Click -= buttonPassKadeKeluar_Click;
            logout.Click -= logout_Click;
            btnClear.Click -= btnClear_Click;
            btnSave.Click -= btnSave_Click;

            button1.Click -= button1_Click;
            button2.Click -= button2_Click;
        }
    }
}
