namespace AdmXmlDb.Management;

public static class Localization
{
    private static readonly Dictionary<string, Dictionary<string, string>> Languages = new()
    {
        ["tr"] = new()
        {
            ["title"] = "XML İşlemci",

            // Menu
            ["menu_file"] = "_Dosya",
            ["menu_config_save"] = "Config Kaydet",
            ["menu_config_load"] = "Config Yükle",
            ["menu_exit"] = "Çıkış",
            ["menu_language"] = "_Dil",
            ["menu_help"] = "_Yardım",
            ["menu_about"] = "Hakkında",

            // Tabs
            ["tab_task_mgmt"] = "Görev Yönetimi",
            ["tab_task_settings"] = "Görev Ayarları",
            ["tab_dashboard"] = "Kontrol Paneli",
            ["tab_general_settings"] = "Genel Ayarlar",
            ["tab_simulation"] = "Simülasyon",

            // Task Management - DB Connection
            ["grp_db_connection"] = "Veritabanı Bağlantısı",
            ["lbl_server"] = "Sunucu",
            ["grp_auth"] = "Kimlik Doğrulama",
            ["radio_win_auth"] = "Windows Kimlik Doğrulama",
            ["radio_sql_auth"] = "SQL Server Kimlik Doğrulama",
            ["grp_login"] = "Giriş Bilgileri",
            ["lbl_username"] = "Kullanıcı Adı",
            ["lbl_password"] = "Şifre",
            ["btn_connect"] = "Bağlan",
            ["lbl_databases"] = "Veritabanları",
            ["lbl_tables"] = "Tablolar",
            ["lbl_columns"] = "Kolonlar",

            // Task Management - Center
            ["btn_load_xml"] = "XML Yükle",
            ["chk_multi_record"] = "Birden fazla döküman düğümü var",
            ["grp_xml_view"] = "XML Görünümü",
            ["grp_tree_view"] = "Ağaç Görünümü",
            ["btn_add_mapping"] = "Eşlemeye Ekle",
            ["tip_add_mapping"] = "Soldan bir kolon ve buradan bir XML düğümü seçip eşlemeye ekleyin",

            // Task Management - Right
            ["grp_input"] = "Girdi",
            ["lbl_preview"] = "Önizleme:",
            ["grp_output"] = "Çıktı",
            ["lbl_file_suffix"] = "Dosya Adı Soneki",
            ["chk_add_datetime"] = "Tarih Saat Ekle",
            ["grp_error"] = "Hata",

            // Task Management - Bottom
            ["grp_mapping"] = "Eşleme",
            ["lbl_task_label"] = "Görev:",
            ["hint_task_name"] = "Görev adı",
            ["btn_new"] = "Yeni",
            ["btn_delete"] = "Sil",
            ["btn_save_task"] = "Görevi Kaydet",
            ["btn_save_mappings"] = "Eşlemeleri Kaydet",
            ["btn_create_config"] = "Config Oluştur",
            ["btn_load_config"] = "Config Yükle",
            ["btn_test"] = "Test",

            // DataGrid - Mapping
            ["col_priority"] = "Öncelik",
            ["col_target_table"] = "Hedef Tablo",
            ["col_column_name"] = "Kolon Adı",
            ["col_data_type"] = "Veri Tipi",
            ["col_xml_node"] = "XML Düğümü",
            ["col_search_term"] = "Arama Terimi",
            ["col_replace_with"] = "Değiştir",
            ["col_xml_default"] = "XML Düğüm Varsayılan",
            ["col_value_template"] = "Değer Şablonu",
            ["col_literal"] = "Sabit",

            // Task Settings
            ["grp_folder_paths"] = "Klasör Yolları",
            ["lbl_prepend"] = "Dosya ön eki (çıktı dosya adına eklenir)",
            ["grp_doc_structure"] = "Döküman Yapısı",
            ["lbl_multi_xpath"] = "Tekrarlanan kayıt için XPath (örn: //item)",
            ["lbl_text_remove"] = "Kayıt sonrası kaldırılacak ifade",
            ["grp_adv_connection"] = "Gelişmiş Bağlantı",
            ["chk_manual_conn"] = "Manuel bağlantı dizesi kullan",
            ["lbl_target_table"] = "Hedef tablo (örn: dbo.ScanDetail)",
            ["btn_get_columns"] = "Kolonları Getir",
            ["grp_target_dir"] = "Hedef Dizin",
            ["lbl_static_base"] = "Statik temel dizin",
            ["lbl_dynamic_comp"] = "Dinamik hedef yolu bileşenleri",
            ["btn_add_xpath"] = "XPath Ekle",
            ["btn_add_static"] = "Statik Ekle",
            ["btn_remove"] = "Sil",
            ["btn_move_up"] = "Yukarı",
            ["btn_move_down"] = "Aşağı",
            ["grp_network_creds"] = "Ağ Kimlik Bilgileri (UNC Yolları)",
            ["lbl_network_creds_info"] = "Giriş/Çıkış/Hata yolları ağ paylaşımında ise (\\\\sunucu\\paylaşım) gereklidir",
            ["lbl_net_username"] = "Windows Kullanıcı Adı (örn: DOMAIN\\kullanici)",
            ["lbl_net_password"] = "Şifre",
            ["grp_scheduling"] = "Zamanlama ve Bildirimler",
            ["lbl_cron"] = "Cron ifadesi (örn: 0 0 * * * ?)",
            ["lbl_error_emails"] = "Hata e-postaları (noktalı virgülle ayırın)",
            ["chk_task_enabled"] = "Görev aktif",
            ["grp_select_task_settings"] = "Görev Seç",
            ["grp_processing"] = "İşleme",
            ["lbl_processing_delay"] = "İşleme gecikmesi (dosya algılandıktan sonraki saniye)",

            // Dashboard
            ["grp_service"] = "AdmXmlDbWorker Servisi",
            ["btn_refresh"] = "Yenile",
            ["btn_refresh_logs"] = "Logları Yenile",
            ["btn_clean_logs"] = "Eski Logları Temizle (90 gün)",
            ["btn_prev"] = "Önceki",
            ["btn_next"] = "Sonraki",
            ["col_task"] = "Görev",
            ["col_filename"] = "Dosya Adı",
            ["col_status"] = "Durum",
            ["col_timestamp"] = "Zaman",
            ["col_message"] = "Detay",

            // General Settings
            ["grp_smtp"] = "SMTP Ayarları (Hata Bildirimleri)",
            ["lbl_smtp_host"] = "SMTP Sunucu",
            ["lbl_port"] = "Port",
            ["chk_use_ssl"] = "SSL Kullan",
            ["lbl_smtp_username"] = "Kullanıcı Adı",
            ["lbl_smtp_password"] = "Şifre",
            ["lbl_sender"] = "Gönderen E-posta",
            ["btn_save"] = "Kaydet",
            ["btn_test_conn"] = "Bağlantı Test Et",

            // Simulation
            ["btn_sim_load_xml"] = "XML Yükle",
            ["lbl_select_task"] = "XPath eşlemeleri için görev seçin",
            ["lbl_parsed_data"] = "Ayrıştırılan Veri",
            ["lbl_gen_sql"] = "Oluşturulan SQL",
            ["btn_dry_run"] = "Test Çalıştırma",
        },

        ["en"] = new()
        {
            ["title"] = "XML Processor",

            // Menu
            ["menu_file"] = "_File",
            ["menu_config_save"] = "Save Config",
            ["menu_config_load"] = "Load Config",
            ["menu_exit"] = "Exit",
            ["menu_language"] = "_Language",
            ["menu_help"] = "_Help",
            ["menu_about"] = "About",

            // Tabs
            ["tab_task_mgmt"] = "Task Management",
            ["tab_task_settings"] = "Task Settings",
            ["tab_dashboard"] = "Dashboard",
            ["tab_general_settings"] = "General Settings",
            ["tab_simulation"] = "Simulation",

            // Task Management - DB Connection
            ["grp_db_connection"] = "Database Connection",
            ["lbl_server"] = "Server",
            ["grp_auth"] = "Authentication",
            ["radio_win_auth"] = "Windows Authentication",
            ["radio_sql_auth"] = "SQL Server Authentication",
            ["grp_login"] = "Login",
            ["lbl_username"] = "Username",
            ["lbl_password"] = "Password",
            ["btn_connect"] = "Connect",
            ["lbl_databases"] = "Databases",
            ["lbl_tables"] = "Tables",
            ["lbl_columns"] = "Columns",

            // Task Management - Center
            ["btn_load_xml"] = "Load XML",
            ["chk_multi_record"] = "Has more than one document node",
            ["grp_xml_view"] = "XML View",
            ["grp_tree_view"] = "Tree View",
            ["btn_add_mapping"] = "Add to Mapping",
            ["tip_add_mapping"] = "Select a Column from left + an XML node here, then click to add mapping",

            // Task Management - Right
            ["grp_input"] = "Input",
            ["lbl_preview"] = "Preview:",
            ["grp_output"] = "Output",
            ["lbl_file_suffix"] = "File Name Suffix",
            ["chk_add_datetime"] = "Add DateTime",
            ["grp_error"] = "Error",

            // Task Management - Bottom
            ["grp_mapping"] = "Mapping",
            ["lbl_task_label"] = "Task:",
            ["hint_task_name"] = "Task name",
            ["btn_new"] = "New",
            ["btn_delete"] = "Delete",
            ["btn_save_task"] = "Save Task",
            ["btn_save_mappings"] = "Save Mappings",
            ["btn_create_config"] = "Create Config",
            ["btn_load_config"] = "Load Config",
            ["btn_test"] = "Test",

            // DataGrid - Mapping
            ["col_priority"] = "Priority",
            ["col_target_table"] = "Target Table",
            ["col_column_name"] = "Column Name",
            ["col_data_type"] = "Data Type",
            ["col_xml_node"] = "XML Node",
            ["col_search_term"] = "Search Term",
            ["col_replace_with"] = "Replace With",
            ["col_xml_default"] = "XML Node Default Value",
            ["col_value_template"] = "Value Template",
            ["col_literal"] = "Literal",

            // Task Settings
            ["grp_folder_paths"] = "Folder Paths",
            ["lbl_prepend"] = "File name prefix (appended to output file name)",
            ["grp_doc_structure"] = "Document Structure",
            ["lbl_multi_xpath"] = "XPath for repeating records (e.g. //item)",
            ["lbl_text_remove"] = "Expression to remove after record",
            ["grp_adv_connection"] = "Advanced Connection",
            ["chk_manual_conn"] = "Use manual connection string",
            ["lbl_target_table"] = "Target table (e.g. dbo.ScanDetail)",
            ["btn_get_columns"] = "Get Columns",
            ["grp_target_dir"] = "Target Directory",
            ["lbl_static_base"] = "Static base directory",
            ["lbl_dynamic_comp"] = "Dynamic target path components",
            ["btn_add_xpath"] = "Add XPath",
            ["btn_add_static"] = "Add Static",
            ["btn_remove"] = "Remove",
            ["btn_move_up"] = "Up",
            ["btn_move_down"] = "Down",
            ["grp_network_creds"] = "Network Credentials (UNC Paths)",
            ["lbl_network_creds_info"] = "Required when Input/Output/Error paths are on a network share (\\\\server\\share)",
            ["lbl_net_username"] = "Windows Username (e.g. DOMAIN\\user)",
            ["lbl_net_password"] = "Password",
            ["grp_scheduling"] = "Scheduling & Notifications",
            ["lbl_cron"] = "Cron expression (e.g. 0 0 * * * ?)",
            ["lbl_error_emails"] = "Error emails (separate with semicolons)",
            ["chk_task_enabled"] = "Task enabled",
            ["grp_select_task_settings"] = "Select Task",
            ["grp_processing"] = "Processing",
            ["lbl_processing_delay"] = "Processing delay (seconds after file detected)",

            // Dashboard
            ["grp_service"] = "AdmXmlDbWorker Service",
            ["btn_refresh"] = "Refresh",
            ["btn_refresh_logs"] = "Refresh Logs",
            ["btn_clean_logs"] = "Clean Old Logs (90 days)",
            ["btn_prev"] = "Previous",
            ["btn_next"] = "Next",
            ["col_task"] = "Task",
            ["col_filename"] = "Filename",
            ["col_status"] = "Status",
            ["col_timestamp"] = "Timestamp",
            ["col_message"] = "Details",

            // General Settings
            ["grp_smtp"] = "SMTP Settings (Error Notifications)",
            ["lbl_smtp_host"] = "SMTP Host",
            ["lbl_port"] = "Port",
            ["chk_use_ssl"] = "Use SSL",
            ["lbl_smtp_username"] = "Username",
            ["lbl_smtp_password"] = "Password",
            ["lbl_sender"] = "Sender Email",
            ["btn_save"] = "Save",
            ["btn_test_conn"] = "Test Connection",

            // Simulation
            ["btn_sim_load_xml"] = "Load XML",
            ["lbl_select_task"] = "Select a task for XPath mappings",
            ["lbl_parsed_data"] = "Parsed Data",
            ["lbl_gen_sql"] = "Generated SQL",
            ["btn_dry_run"] = "Dry Run (Test)",
        }
    };

    public static Dictionary<string, string> GetDictionary(string lang)
    {
        return Languages.TryGetValue(lang, out var dict) ? dict : Languages["en"];
    }
}
