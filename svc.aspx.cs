using System;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using System.Web.Script.Serialization;
using System.Text.RegularExpressions;
using System.Net;
using System.Collections;
using System.Configuration;
using Microsoft.SharePoint;
using Microsoft.SharePoint.Client;
using Microsoft.SharePoint.Administration.Claims;
using Microsoft.SharePoint.WebControls;
using Microsoft.SharePoint.Administration;
using System.IO;
using System.ServiceModel.Web;
using System.Data;
using System.Data.SqlClient;
using Microsoft.SharePoint.Utilities;
using System.DirectoryServices.AccountManagement;

public partial class svc : System.Web.UI.Page
{
    private const string OpNameParameter = "op";

    private const string JsonErrorFmt =
        @"{{
                  ""error"" : {{
                    ""code"" : ""{0}"",
                    ""message"" : {{
                      ""lang"" : ""{4}"",
                      ""value"" : ""Method {1}: {2}{3}""
                    }}
                  }}
                }}";


    // ReSharper disable CoVariantArrayConversion

    /// <summary>
    ///    Handles the Load event of the Page control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">
    ///    The <see cref="System.EventArgs" /> instance containing the event data.
    /// </param>
    protected void Page_Load(object sender, EventArgs e)
    {
        var opName = "Unknown";
        try
        {
            
            opName = Request.Params[OpNameParameter];
            if (string.IsNullOrEmpty(opName))
                throw new Exception(string.Format("{0} parameter not found in request!", OpNameParameter));

            var opMethodInfo = GetType().GetMethod(opName);
            if (opMethodInfo == null)
                throw new Exception("Operation not found!");

            var parameters = opMethodInfo.GetParameters().Select(pn => Convert.ChangeType(Request.Params[pn.Name], pn.ParameterType)).ToArray();
            var opResult = parameters.Length > 0 ? opMethodInfo.Invoke(this, parameters) : opMethodInfo.Invoke(this, null);
            if (opName != "DownloadFileLocal")
            {
                WriteResponse(200, opResult);
            }
        }
        catch (Exception ex)
        {
            var exResult = string.Format(JsonErrorFmt, 400, opName, ex.Message, ex.InnerException != null ? string.Format(" &raquo; {0}", ex.InnerException.Message) : string.Empty, string.Empty);
            WriteResponse(400, exResult);
        }
        Response.End();
    }

    #region Private Helper Methods

    // ReSharper restore CoVariantArrayConversion

    /// <summary>
    ///    Writes the response.
    /// </summary>
    /// <param name="httpStatusCode">The HTTP status code.</param>
    /// <param name="opResult">The operation result.</param>
    private void WriteResponse(int httpStatusCode, object opResult)
    {
        Response.Clear();
        Response.StatusCode = httpStatusCode;
        Response.ContentType = "application/json; charset=utf-8";
        Response.Write(opResult);
    }

    private static string CreateJsonResponse(object data)
    {
        return CreateJsonResponse(data, "callbackJsonp");
    }

    /// <summary>
    ///    Creates the json response.
    /// </summary>
    /// <param name="data">The data.</param>
    /// <returns></returns>
    private static string CreateJsonResponse(object data, string callbackMethod)
    {
        var js = new JavaScriptSerializer();
        string results;
        if (data is IList)
        {
            var list = (data as IList);
            var enumerable = list as object[] ?? list.Cast<object>().ToArray();
            var count = enumerable.Count();
            results = js.Serialize(new
            {
                d = new
                {
                    results = enumerable,
                    __count = count
                }
            });
        }
        else
        {
            results = js.Serialize(new
            {
                d = new
                {
                    results = data
                }
            });
        }

        return callbackMethod + "(" + results + ");";
    }

    private string GetValue(object obj)
    {
        try
        {
            return obj.ToString();
        }
        catch
        {
            return "";
        }
    }

    private static SPUser GetSPUser(SPListItem item, string key)
    {
        SPFieldUser field = item.Fields[key] as SPFieldUser;

        if (field != null && item[key] != null)
        {
            SPFieldUserValue fieldValue = field.GetFieldValue(item[key].ToString()) as SPFieldUserValue;
            if (fieldValue != null)
            {
                return fieldValue.User;
            }
        }
        return null;
    }

    private static string decodeAuthentication(string encodedAuthInfo)
    {
        try
        {
            System.Text.UTF8Encoding encoder = new System.Text.UTF8Encoding();
            System.Text.Decoder utf8Decode = encoder.GetDecoder();

            byte[] todecode_byte = Convert.FromBase64String(encodedAuthInfo);
            int charCount = utf8Decode.GetCharCount(todecode_byte, 0, todecode_byte.Length);
            char[] decoded_char = new char[charCount];
            utf8Decode.GetChars(todecode_byte, 0, todecode_byte.Length, decoded_char, 0);

            return new String(decoded_char);
        }
        catch {}

        return "";
    }

    #endregion

    #region OP::Authenticate

    public class LoginInfo
    {
        public bool issuccess = false;
        public string name = "";
        public string email = "";
        public string phone = "0000000000";
    }

    public string Authenticate(string authInfo, string currentURL, string SPUrl, string callback)
    {
        LoginInfo loginInfo = new LoginInfo();
        try
        {
            string loginString = decodeAuthentication(authInfo);
            if (loginString.IndexOf(':') > 0)
            {
                string[] tokens = loginString.Split(":".ToCharArray());
                string spUsername = tokens[0];
                PrincipalContext pc = new PrincipalContext(ContextType.Domain, spUsername.Split('\\')[0]);

                bool isValid = pc.ValidateCredentials(spUsername.Split('\\')[1], tokens[1]);
                if (isValid)
                {

                    SPSecurity.RunWithElevatedPrivileges(delegate()
                    {
                        using (SPSite site = new SPSite(SPUrl))
                        {
                            using (SPWeb thisWeb = site.OpenWeb())
                            {
                                site.RootWeb.AllowUnsafeUpdates = true;
                                SPUser user = site.RootWeb.EnsureUser(spUsername);
                                loginInfo.name = user.Name;
                                loginInfo.email = user.Email;
                                loginInfo.issuccess = true;
                                site.RootWeb.AllowUnsafeUpdates = false;
                            }
                        }
                    });


                    var url = System.Configuration.ConfigurationManager.AppSettings["GetUserInfoURL"].ToString().Replace("[EMAILADDRESS]", loginInfo.email);
                    var syncClient = new WebClient();
                    var content = syncClient.DownloadString(url);

                    string[] values = content.Split(",".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
                    foreach (string str in values)
                    {
                        if (str.StartsWith("\"WorkPhone\":", StringComparison.CurrentCultureIgnoreCase))
                            loginInfo.phone = str.ToLower().Replace("\"", "").Replace("workphone", "").Replace(":", "");
                    }
                }
            }
        }
        catch (Exception ex)
        {
        }

        return CreateJsonResponse(loginInfo, callback);
    }

    #endregion

    //#region OP:Login

    //public void Login(string SPUrl, string authInfo)
    //{
    //    this.AddLog(SPUrl, "LOGIN", null, authInfo);
    //}

    //#endregion

    #region OP::GetAllCatalogs

//    public string GetAllCatalogs(string SPUrl, int position, string modality, string documentType)
//    {
//        List<Catalog> documents = new List<Catalog>();
//        using (SPSite site = new SPSite(SPUrl))
//        {
//            using (SPWeb web = site.OpenWeb())
//            {
//                SPList mList = web.Lists["DESRSystems"];
//                SPQuery camlQuery = new SPQuery();
//                if (modality == "All" && documentType == "All")
//                {
//                    camlQuery.Query = @"<Where>
//                                      <IsNotNull>
//                                         <FieldRef Name='Modality' />
//                                      </IsNotNull>
//                                   </Where>";
//                }
//                else
//                {
//                    if (modality == "All")
//                    {
//                        camlQuery.Query = string.Format(@"<Where>
//                                          <And>
//                                             <IsNotNull>
//                                                <FieldRef Name='Modality' />
//                                             </IsNotNull>
//                                             <Eq>
//                                                <FieldRef Name='SystemType' />
//                                                <Value Type='Text'>{0}</Value>
//                                             </Eq>
//                                          </And>
//                                       </Where>", documentType);
//                    }
//                    else if (documentType == "All")
//                    {
//                        camlQuery.Query = string.Format(@"<Where>
//                                          <Eq>
//                                             <FieldRef Name='Modality' />
//                                             <Value Type='Choice'>{0}</Value>
//                                          </Eq>
//                                       </Where>", modality);
//                    }
//                    else
//                    {
//                        camlQuery.Query = string.Format(@"<Where>
//                                                      <And>
//                                                         <Eq>
//                                                            <FieldRef Name='Modality' />
//                                                            <Value Type='Choice'>{0}</Value>
//                                                         </Eq>
//                                                         <Eq>
//                                                            <FieldRef Name='SystemType' />
//                                                            <Value Type='Text'>{1}</Value>
//                                                         </Eq>
//                                                      </And>
//                                                   </Where>", modality, documentType);
//                    }
//                }
//                camlQuery.RowLimit = 20 * Convert.ToUInt32(position);

//                SPListItemCollection listItems = mList.GetItems(camlQuery);
//                foreach (SPListItem item in listItems)
//                {
//                    Catalog cat = new Catalog
//                    {
//                        Modality = item["Modality"] + "",
//                        Product = item["Title"] + "",
//                        SystemType = item["SystemType"] + "",
//                        MCSS = item["MCSS"] + "",
//                        Software_x0020_Version = item["Software_x0020_Version"] + "",
//                        Revision_x0020_Level = item["Revision_x0020_Level"] + "",
//                        System_x0020_Date = item["System_x0020_Date"] + "",
//                        ID = item["ID"] + "",
//                        ImageURL = "", //item["ImageURL"] + ""
//                        Creator = GetSPValue(item["Created By"]).Substring(GetSPValue(item["Created By"]).IndexOf('#') + 1)
//                    };
//                    if (item["ImageURL"] + "" != "")
//                    {
//                        cat.ImageURL = DownloadFile(item["ImageURL"] + "");
//                        //cat.ImageURL = Path.GetFileName(item["ImageURL"]).ToString();
//                    }
//                    documents.Add(cat);
//                }
//            }
//        }
//        return CreateJsonResponse(documents.ToArray());
//    }

//    public string GetNewestCatalogs(string SPUrl, int position, string modality, string documentType)
//    {
//        List<Catalog> documents = new List<Catalog>();
//        using (SPSite site = new SPSite(SPUrl))
//        {
//            using (SPWeb web = site.OpenWeb())
//            {
//                SPList mList = web.Lists["DESRSystems"];
//                SPQuery camlQuery = new SPQuery();
//                if (modality == "All" && documentType == "All")
//                {
//                    camlQuery.Query = @"<Where>
//                                      <IsNotNull>
//                                         <FieldRef Name='Modality' />
//                                      </IsNotNull>
//                                   </Where>
//                                    <OrderBy>
//                                        <FieldRef Name='System_x0020_Date' Ascending='FALSE' />
//                                    </OrderBy>";
//                }
//                else
//                {
//                    if (modality == "All")
//                    {
//                        camlQuery.Query = string.Format(@"<Where>
//                                          <And>
//                                             <IsNotNull>
//                                                <FieldRef Name='Modality' />
//                                             </IsNotNull>
//                                             <Eq>
//                                                <FieldRef Name='SystemType' />
//                                                <Value Type='Text'>{0}</Value>
//                                             </Eq>
//                                          </And>
//                                       </Where>
//                                    <OrderBy>
//                                        <FieldRef Name='System_x0020_Date' Ascending='FALSE' />
//                                    </OrderBy>", documentType);
//                    }
//                    else if (documentType == "All")
//                    {
//                        camlQuery.Query = string.Format(@"<Where>
//                                          <Eq>
//                                             <FieldRef Name='Modality' />
//                                             <Value Type='Choice'>{0}</Value>
//                                          </Eq>
//                                       </Where>
//                                    <OrderBy>
//                                        <FieldRef Name='System_x0020_Date' Ascending='FALSE' />
//                                    </OrderBy>", modality);
//                    }
//                    else
//                    {
//                        camlQuery.Query = string.Format(@"<Where>
//                                                      <And>
//                                                         <Eq>
//                                                            <FieldRef Name='Modality' />
//                                                            <Value Type='Choice'>{0}</Value>
//                                                         </Eq>
//                                                         <Eq>
//                                                            <FieldRef Name='SystemType' />
//                                                            <Value Type='Text'>{1}</Value>
//                                                         </Eq>
//                                                      </And>
//                                                   </Where>
//                                    <OrderBy>
//                                        <FieldRef Name='System_x0020_Date' Ascending='FALSE' />
//                                    </OrderBy>", modality, documentType);
//                    }
//                }
//                camlQuery.RowLimit = 20 * Convert.ToUInt32(position);

//                SPListItemCollection listItems = mList.GetItems(camlQuery);
//                foreach (SPListItem item in listItems)
//                {
//                    Catalog cat = new Catalog
//                    {
//                        Modality = item["Modality"] + "",
//                        Product = item["Title"] + "",
//                        SystemType = item["SystemType"] + "",
//                        MCSS = item["MCSS"] + "",
//                        Software_x0020_Version = item["Software_x0020_Version"] + "",
//                        Revision_x0020_Level = item["Revision_x0020_Level"] + "",
//                        System_x0020_Date = item["System_x0020_Date"] + "",
//                        ID = item["ID"] + "",
//                        ImageURL = "", //item["ImageURL"] + ""
//                        Creator = GetSPValue(item["Created By"]).Substring(GetSPValue(item["Created By"]).IndexOf('#') + 1)
//                    };
//                    if (item["ImageURL"] + "" != "")
//                    {
//                        cat.ImageURL = DownloadFile(item["ImageURL"] + "");
//                    }
//                    documents.Add(cat);
//                }
//            }
//        }
//        return CreateJsonResponse(documents.ToArray());
//    }

    public string SearchCatalogs(string SPUrl, string searchText, string modality, string documentType, string callback, string authInfo)
    {
        List<Catalog> documents = new List<Catalog>();
        try
        {
            SPSecurity.RunWithElevatedPrivileges(delegate()
            {
                using (SPSite site = new SPSite(SPUrl))
                {
                    using (SPWeb web = site.OpenWeb())
                    {
                        SPList mList = web.Lists["DESRSystems"];
                        SPQuery camlQuery = new SPQuery();

                        string searchQuery = "<IsNotNull><FieldRef Name='ID'></FieldRef></IsNotNull>";
                        if (!string.IsNullOrEmpty(searchText.Trim()))
                            searchQuery = "<Or><Or><Or><Contains><FieldRef Name='Title' /><Value Type='Text'>" + searchText + "</Value></Contains><Contains><FieldRef Name='Software_x0020_Version' /><Value Type='Text'>" + searchText + "</Value></Contains></Or><Contains><FieldRef Name='Modality' /><Value Type='Choice'>" + searchText + "</Value></Contains></Or><Contains><FieldRef Name='SystemType' /><Value Type='Text'>" + searchText + "</Value></Contains></Or>";

                        if (modality == "All" && documentType == "All")
                        {
                            camlQuery.Query = "<Where>" + searchQuery + "</Where>";
                        }
                        else
                        {
                            if (modality == "All")
                            {
                                camlQuery.Query = "<Where><And>" + searchQuery + "<Eq><FieldRef Name='SystemType' /><Value Type='Text'>" + documentType + "</Value></Eq></And></Where>";
                            }
                            else if (documentType == "All")
                            {
                                camlQuery.Query = "<Where><And>" + searchQuery + "<Eq><FieldRef Name='Modality' /><Value Type='Choice'>" + modality + "</Value></Eq></And></Where>";
                            }
                            else
                            {
                                camlQuery.Query = "<Where><And><And>" + searchQuery + "<Eq><FieldRef Name='Modality' /><Value Type='Choice'>" + modality + "</Value></Eq></And><Eq><FieldRef Name='SystemType' /><Value Type='Text'>" + documentType + "</Value></Eq></And></Where>";
                            }
                        }
                        SPListItemCollection listItems = mList.GetItems(camlQuery);
                        foreach (SPListItem item in listItems)
                        {
                            Catalog cat = new Catalog
                            {
                                Modality = item["Modality"] + "",
                                Product = item["Title"] + "",
                                SystemType = item["SystemType"] + "",
                                MCSS = item["MCSS"] + "",
                                Software_x0020_Version = item["Software_x0020_Version"] + "",
                                Revision_x0020_Level = item["Revision_x0020_Level"] + "",
                                System_x0020_Date = item["System_x0020_Date"] + "",
                                ID = item["ID"] + "",
                                ImageURL = "", // item["ImageURL"] + ""
                                Creator = GetSPValue(item["Created By"]).Substring(GetSPValue(item["Created By"]).IndexOf('#') + 1)
                            };
                            if (item["ImageURL"] + "" != "")
                            {
                                cat.ImageURL = "DownloadedFiles/" + DownloadFile(item["ImageURL"] + "");
                            }
                            documents.Add(cat);
                        }
                    }
                }
            });

            this.AddLog(SPUrl, "SEARCHED", searchText, authInfo);
        }
        catch { }
        
        
        return CreateJsonResponse(documents.ToArray(), callback);
    }

    public string GetCatalogById(string SPUrl, int id, string authInfo, string callback)
    {
        List<Catalog> documents = new List<Catalog>();
        try
        {
            SPSecurity.RunWithElevatedPrivileges(delegate()
            {
                using (SPSite site = new SPSite(SPUrl))
                {
                    using (SPWeb web = site.OpenWeb())
                    {
                        SPList mList = web.Lists["DESRSystems"];
                        SPListItem item = mList.GetItemById(id);
                        Catalog cat = new Catalog
                        {
                            Modality = item["Modality"] + "",
                            Product = item["Title"] + "",
                            SystemType = item["SystemType"] + "",
                            MCSS = item["MCSS"] + "",
                            Software_x0020_Version = item["Software_x0020_Version"] + "",
                            Revision_x0020_Level = item["Revision_x0020_Level"] + "",
                            System_x0020_Date = item["System_x0020_Date"] + "",
                            ID = item["ID"] + "",
                            ImageURL = "" //item["ImageURL"] + ""
                        };
                        if (item["ImageURL"] + "" != "")
                        {
                            cat.ImageURL = DownloadFile(item["ImageURL"] + "");
                        }
                        documents.Add(cat);
                    }
                }
            });
        }
        catch { }
        return CreateJsonResponse(documents.ToArray(), callback);
    }

    #endregion

    #region OP:AddStatus
    public string AddStatus(string SPUrl, int recordId, string ControlPanelLayout, string ModalityWorkListEmpty, 
        string AllSoftwareLoadedAndFunctioning, string IfNoExplain, string NPDPresetsOnSystem, 
        string HDDFreeOfPatientStudies, string DemoImagesLoadedOnHardDrive, string SystemPerformedAsExpected, 
        string AnyIssuesDuringDemo, string wasServiceContacted, string ConfirmModalityWorkListRemoved, 
        string ConfirmSystemHDDEmptied, string LayoutChangeExplain, string Comments, string WorkPhone, 
        string SystemPerformedNotAsExpectedExplain, string IsFinal, string authInfo, string callback, string statusId)
    {
        string id = null;
        try
        {
            WorkPhone = WorkPhone.Substring(0, 3) + "-" + WorkPhone.Substring(3, 3) + "-" + WorkPhone.Substring(6);

            string messageBody = "";
            string messageSubject = "";
            string plannerEmail = "";
            string appManagersEmails = "";
            SPUserToken currentUserToken = null;
            bool isSendEmail = false;

            SPSecurity.RunWithElevatedPrivileges(delegate()
            {
                using (SPSite site = new SPSite(SPUrl))
                {
                    using (SPWeb web = site.OpenWeb())
                    {
                        string loginString = decodeAuthentication(authInfo);
                        if (loginString.IndexOf(':') > 0)
                        {
                            web.AllowUnsafeUpdates = true;
                            SPUser currentUser = web.EnsureUser(loginString.Substring(0, loginString.IndexOf(':')));

                            if (currentUser != null)
                            {
                                SPList mList = web.Lists["DESRSystems"];
                                SPListItem item = mList.GetItemById(recordId);
                                SPList desrList = web.Lists["DESR"];
                                web.AllowUnsafeUpdates = true;

                                //update desrsystem list
                                item["MCSS"] = currentUser;
                                item["System_x0020_Date"] = DateTime.Today.ToString();
                                item.Update();
                                //end update

                                SPListItem desrItem = null;
                                int _sid;
                                if (!string.IsNullOrEmpty(statusId) && int.TryParse(statusId, out _sid))
                                    desrItem = desrList.GetItemById(_sid);
                                if (desrItem == null)
                                    desrItem = desrList.AddItem();

                                desrItem["Serial_x0020_Number"] = item["Title"];
                                desrItem["Software_x0020_Version"] = item["Software_x0020_Version"];
                                desrItem["Revision_x0020_Level"] = item["Revision_x0020_Level"];
                                desrItem["System_x0020_Date"] = item["System_x0020_Date"];
                                desrItem["Modality"] = item["Modality"];
                                desrItem["SystemType"] = item["SystemType"];
                                desrItem["MCSS"] = item["MCSS"];
                                desrItem["ControlPanelLayout"] = ControlPanelLayout;
                                desrItem["ModalityWorkListEmpty"] = ModalityWorkListEmpty;
                                desrItem["AllSoftwareLoadedAndFunctioning"] = AllSoftwareLoadedAndFunctioning;
                                desrItem["IfNoExplain"] = IfNoExplain;
                                desrItem["NPDPresetsOnSystem"] = NPDPresetsOnSystem;
                                desrItem["HDDFreeOfPatientStudies"] = HDDFreeOfPatientStudies;
                                desrItem["DemoImagesLoadedOnHardDrive"] = DemoImagesLoadedOnHardDrive;
                                desrItem["SystemPerformedAsExpected"] = SystemPerformedAsExpected;
                                desrItem["SystemPerformedNotAsExpectedExplain"] = SystemPerformedNotAsExpectedExplain;
                                desrItem["AnyIssuesDuringDemo"] = AnyIssuesDuringDemo;
                                desrItem["wasServiceContacted"] = wasServiceContacted;
                                desrItem["ConfirmModalityWorkListRemoved"] = ConfirmModalityWorkListRemoved;
                                desrItem["ConfirmSystemHDDEmptied"] = ConfirmSystemHDDEmptied;
                                desrItem["LayoutChangeExplain"] = LayoutChangeExplain;
                                desrItem["Comments"] = Comments;
                                desrItem["IsFinal"] = IsFinal;
                                desrItem["Author"] = currentUser;
                                desrItem["Editor"] = currentUser;
                                desrItem.Update();
                                id = desrItem["ID"] + "";
                                web.AllowUnsafeUpdates = false;
                                SPUser css = currentUser;


                                string SystemDate = item["System_x0020_Date"].ToString();
                                SystemDate = ((SystemDate != null && SystemDate != "") ? Convert.ToDateTime(SystemDate).ToShortDateString() : "");
                                messageBody = ""; // "<html><head><style>body{font-size:12.0pt;font-family:'Calibri','sans-serif';}p{margin-right:0in;margin-left:0in;font-size:12.0pt;font-family:'Calibri','serif';}</style></head><body ><div class=WordSection1>&nbsp;<table border=0 cellspacing=0 cellpadding=0 style='width:623;'> <tr>  <td colspan=2 valign=top>  This is a system generated email to notify you about a demo equipment’s critical status.  </td> </tr> <tr>  <td colspan=2 valign=top >  &nbsp;  </td> </tr> <tr>  <td colspan=2 valign=top >  <b><u>System information</u></b>  </td> </tr> <tr>  <td valign=top >  System type:  </td>  <td valign=top >" + item["SystemType"] + "</td> </tr> <tr>  <td valign=top >  System serial number:  </td>  <td valign=top >  " + item["Title"] + "  </td> </tr> <tr>  <td valign=top >Software version:  </td>  <td valign=top > " + item["Software_x0020_Version"] + "  </td> </tr> <tr>  <td valign=top >  Revision Level:  </td>  <td valign=top >  " + item["Revision_x0020_Level"] + "  </td> </tr> <tr>  <td valign=top >  Date:  </td>  <td  valign=top >  " + SystemDate + "  </td> </tr> <tr>  <td valign=top >  CSS:  </td>  <td valign=top >  " + css.Name + "  </td> </tr><tr>  <td valign=top >  Comments:  </td>  <td valign=top >  " + Comments + "  </td> </tr> <tr>  <td valign=top >  &nbsp;  </td>  <td valign=top >  &nbsp;  </td> </tr> <tr>  <td colspan=2 valign=top >  <b><u>System condition on arrival</u></b>  </td> </tr> <tr>  <td valign=top >  Control panel layout:  </td>  <td valign=top >  " + ControlPanelLayout + "  </td> </tr><tr>  <td valign=top >  Explain if changed:  </td>  <td valign=top >  " + LayoutChangeExplain + "  </td> </tr> <tr>  <td valign=top >  Modality work list empty:  </td>  <td valign=top >  " + ModalityWorkListEmpty + "  </td> </tr> <tr>  <td valign=top >  All software loaded and functioning:  </td>  <td valign=top >  " + AllSoftwareLoadedAndFunctioning + "  </td> </tr> <tr>  <td valign=top >  Please explain:  </td>  <td valign=top >  " + IfNoExplain + "  </td> </tr> <tr>  <td valign=top >  NPD presets on system:  </td>  <td valign=top >  " + NPDPresetsOnSystem + "  </td> </tr> <tr>  <td valign=top >  HDD free of patients studies:  </td>  <td valign=top >  " + HDDFreeOfPatientStudies + "  </td> </tr> <tr>  <td valign=top >  Demo images loaded on hard drive:  </td>  <td valign=top >  " + DemoImagesLoadedOnHardDrive + "  </td> </tr> <tr>  <td valign=top >  &nbsp;  </td>  <td valign=top >  &nbsp;  </td> </tr> <tr>  <td colspan=2 valign=top >  <b><u>Before leaving customer site</u></b>  </td> </tr> <tr>  <td valign=top >  System performed as expected:  </td>  <td valign=top >  " + SystemPerformedAsExpected + "  </td> </tr> <tr>  <td valign=top>  Were any issues discovered with system during demo</span>:  </td>  <td valign=top>    " + AnyIssuesDuringDemo + "  </td> </tr> <tr>  <td valign=top>  Was service contacted:  </td>  <td valign=top>    " + wasServiceContacted + "  </td> </tr> <tr>  <td valign=top>  Confirm modality work list removed from system:  </td>  </span>  <td valign=top>    " + ConfirmModalityWorkListRemoved + "  </td> </tr> <tr>  <td valign=top>  Confirm system HDD emptied of all patient studies:  </td>  </span>  <td valign=top >    " + ConfirmSystemHDDEmptied + "  </td> </tr> <tr>  <td valign=top >  &nbsp;  </td>  <td valign=top >    &nbsp;  </td> </tr> <tr>  <td valign=top >  <b><u>Specialist Information</u></b>  </td>  <td valign=top >    &nbsp;  </td> </tr> <tr>  <td valign=top >  " + web.CurrentUser.Name + "  </td>  <td valign=top >    &nbsp;  </td> </tr> <tr>  <td valign=top>  " + WorkPhone + "   </td>  <td valign=top >    &nbsp;  </td> </tr> <tr>  <td valign=top >  " + web.CurrentUser.Email.ToLower() + "  </td>  <td valign=top >    &nbsp;  </td> </tr> <tr>  <td valign=top >  &nbsp;  </td>  <td valign=top >    &nbsp;  </td> </tr> <tr>  <td valign=top >  &nbsp;  </td>  <td valign=top >    &nbsp;  </td> </tr></table></div></body></html>";

                                messageBody += "<html><head><style>body{font-size:12.0pt;font-family:'Calibri','sans-serif';}p{margin-right:0in;margin-left:0in;font-size:12.0pt;font-family:'Calibri','serif';}</style></head><body >";
                                messageBody += "<div class=WordSection1>&nbsp;<table border=0 cellspacing=0 cellpadding=0 style='width:623;'> ";
                                messageBody += "<tr><td colspan=2 valign=top>  This is a system generated email to notify you about a demo equipment’s critical status.  </td></tr>";
                                messageBody += "<tr><td colspan=2 valign=top >  &nbsp;  </td></tr>";
                                messageBody += "<tr><tdcolspan=2 valign=top >  <b><u>System information</u></b>  </td></tr>";
                                messageBody += "<tr><tdvalign=top >  System type:  </td>  <td valign=top >" + item["SystemType"] + "</td> </tr>";
                                messageBody += "<tr><tdvalign=top >  System serial number:  </td>  <td valign=top >  " + item["Title"] + "  </td></tr>";
                                messageBody += "<tr><tdvalign=top >Software version:  </td>  <td valign=top > " + item["Software_x0020_Version"] + "  </td></tr>";
                                messageBody += "<tr><tdvalign=top >  Revision Level:  </td>  <td valign=top >  " + item["Revision_x0020_Level"] + "  </td></tr>";
                                messageBody += "<tr><tdvalign=top >  Date:  </td>  <td  valign=top >  " + SystemDate + "  </td></tr>";
                                messageBody += "<tr><tdvalign=top >  CSS:  </td>  <td valign=top >  " + css.Name + "  </td></tr>";
                                messageBody += "<tr><tdvalign=top >  Comments:  </td>  <td valign=top >  " + Comments + "  </td></tr>";
                                messageBody += "<tr><tdvalign=top >  &nbsp;  </td>  <td valign=top >  &nbsp;  </td></tr>";
                                messageBody += "<tr><tdcolspan=2 valign=top >  <b><u>System condition on arrival</u></b>  </td></tr>";
                                messageBody += "<tr><tdvalign=top >  Control panel layout:  </td>  <td valign=top >  " + ControlPanelLayout + "  </td></tr>";
                                messageBody += "<tr><tdvalign=top >  Explain if changed:  </td>  <td valign=top >  " + LayoutChangeExplain + "  </td></tr>";
                                messageBody += "<tr><tdvalign=top >  Modality work list empty:  </td>  <td valign=top >  " + ModalityWorkListEmpty + "  </td></tr>";
                                messageBody += "<tr><tdvalign=top >  All software loaded and functioning:  </td>  <td valign=top >  " + AllSoftwareLoadedAndFunctioning + "  </td></tr>";
                                messageBody += "<tr><tdvalign=top >  &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Please explain:  </td>  <td valign=top >  " + IfNoExplain + "  </td></tr>";
                                messageBody += "<tr><tdvalign=top >  NPD presets on system:  </td>  <td valign=top >  " + NPDPresetsOnSystem + "  </td></tr>";
                                messageBody += "<tr><tdvalign=top >  HDD free of patients studies:  </td>  <td valign=top >  " + HDDFreeOfPatientStudies + "  </td></tr>";
                                messageBody += "<tr><tdvalign=top >  Demo images loaded on hard drive:  </td>  <td valign=top >  " + DemoImagesLoadedOnHardDrive + "  </td></tr>";
                                messageBody += "<tr><tdvalign=top >  &nbsp;  </td>  <td valign=top >  &nbsp;  </td></tr>";
                                messageBody += "<tr><tdcolspan=2 valign=top >  <b><u>Before leaving customer site</u></b>  </td></tr>";
                                messageBody += "<tr><tdvalign=top >  System performed as expected:  </td>  <td valign=top >  " + SystemPerformedAsExpected + "  </td></tr>";
                                messageBody += "<tr><tdvalign=top >  &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Please explain:  </td>  <td valign=top >  " + SystemPerformedNotAsExpectedExplain + "  </td></tr>";
                                messageBody += "<tr><tdvalign=top>  Were any issues discovered with system during demo</span>:  </td>  <td valign=top>    " + AnyIssuesDuringDemo + "  </td></tr>";
                                messageBody += "<tr><tdvalign=top>  Was service contacted:  </td>  <td valign=top>    " + wasServiceContacted + "  </td></tr>";
                                messageBody += "<tr><tdvalign=top>  Confirm that you have removed modality work list from system:  </td>  </span>  <td valign=top>    " + ConfirmModalityWorkListRemoved + "  </td></tr>";
                                messageBody += "<tr><tdvalign=top>  Confirm that you have emptied system HDD emptied of all patient studies:  </td>  </span>  <td valign=top >    " + ConfirmSystemHDDEmptied + "  </td></tr>";
                                messageBody += "<tr><tdvalign=top >  &nbsp;  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                messageBody += "<tr><tdvalign=top >  <b><u>Specialist Information</u></b>  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                messageBody += "<tr><tdvalign=top >  " + currentUser.Name + "  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                messageBody += "<tr><tdvalign=top>  " + WorkPhone + "   </td>  <td valign=top >    &nbsp;  </td></tr>";
                                messageBody += "<tr><tdvalign=top >  " + currentUser.Email.ToLower() + "  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                messageBody += "<tr><tdvalign=top >  &nbsp;  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                messageBody += "<tr><tdvalign=top >  &nbsp;  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                messageBody += "</table></div></body></html>";

                                SPList emailsList = web.Lists["DESREmailRecepients"];
                                plannerEmail = "";
                                appManagersEmails = "";
                                foreach (SPListItem emailItem in emailsList.Items)
                                {
                                    if (Convert.ToString(emailItem["Title"]).ToLower() == "planner")
                                    {
                                        plannerEmail = Convert.ToString(emailItem["Email"]);
                                    }
                                    if (Convert.ToString(emailItem["Title"]).ToLower() == Convert.ToString(item["Modality"]).ToLower())
                                    {
                                        appManagersEmails += Convert.ToString(emailItem["Email"]) + ",";
                                    }
                                }

                                if (appManagersEmails.EndsWith(",") || appManagersEmails.EndsWith(";"))
                                    appManagersEmails = appManagersEmails.Substring(0, appManagersEmails.Length - 1);


                                if (ModalityWorkListEmpty == "No" ||
                                    AllSoftwareLoadedAndFunctioning == "No" ||
                                    NPDPresetsOnSystem == "No" ||
                                    HDDFreeOfPatientStudies == "No" ||
                                    DemoImagesLoadedOnHardDrive == "No" ||
                                    SystemPerformedAsExpected == "No" ||
                                    AnyIssuesDuringDemo == "Yes")
                                {

                                    messageSubject = "Demo Equipment Status Alert - " + item["SystemType"] + " - " + item["Title"];
                                    currentUserToken = currentUser.UserToken;
                                    isSendEmail = true;
                                }
                            }
                        }
                    }
                }
            });

            if (isSendEmail && IsFinal.Equals("Yes", StringComparison.CurrentCultureIgnoreCase))
            {
                using (SPSite impsite = new SPSite(SPUrl, currentUserToken))
                {
                    using (SPWeb impweb = impsite.OpenWeb())
                    {
                        StringDictionary headers = new StringDictionary();
                        headers.Add("to", appManagersEmails);
                        headers.Add("cc", plannerEmail);
                        headers.Add("from", "portaladmin@tams.com");
                        headers.Add("subject", messageSubject);


                        SPUtility.SendEmail(impweb, headers, messageBody);
                    }
                }
            }

            this.AddLog(SPUrl, "ADD STATUS", null, authInfo);
        }
        catch { }

        List<string> retValues = new List<string>();
        retValues.Add(id);

        return CreateJsonResponse(retValues.ToArray(), callback);
    }

    public string AddNewStatus(string SPUrl, string SerialNumber, string SoftwareVersion, string RevisionLevel, string SystemType, string Modality, 
        string ControlPanelLayout, string ModalityWorkListEmpty, string AllSoftwareLoadedAndFunctioning, string IfNoExplain, 
        string NPDPresetsOnSystem, string HDDFreeOfPatientStudies, string DemoImagesLoadedOnHardDrive, string SystemPerformedAsExpected, 
        string AnyIssuesDuringDemo, string wasServiceContacted, string ConfirmModalityWorkListRemoved, string ConfirmSystemHDDEmptied,
        string LayoutChangeExplain, string Comments, string WorkPhone, string SystemPerformedNotAsExpectedExplain, string authInfo, string callback, string IsFinal, string statusId)
    {
        string id = null;

        try
        {
            WorkPhone = WorkPhone.Substring(0, 3) + "-" + WorkPhone.Substring(3, 3) + "-" + WorkPhone.Substring(6);

            bool isSendEmail = false;
            SPUserToken currentUserToken = null;
            string plannerEmail = "";
            string appManagersEmails = "";
            string messageSubject = "";
            string messageBody = "";

            SPSecurity.RunWithElevatedPrivileges(delegate()
            {
                using (SPSite site = new SPSite(SPUrl))
                {
                    using (SPWeb web = site.OpenWeb())
                    {
                        string loginString = decodeAuthentication(authInfo);
                        if (loginString.IndexOf(':') > 0)
                        {
                            web.AllowUnsafeUpdates = true;
                            SPUser currentUser = web.EnsureUser(loginString.Substring(0, loginString.IndexOf(':')));

                            if (currentUser != null)
                            {
                                SPList desrList = web.Lists["DESR"];
                                web.AllowUnsafeUpdates = true;
                                ;
                                SPListItem desrItem = null;
                                int _sid;
                                if (!string.IsNullOrEmpty(statusId) && int.TryParse(statusId, out _sid))
                                    desrItem = desrList.GetItemById(_sid);
                                if (desrItem == null)
                                    desrItem = desrList.AddItem();

                                desrItem["Serial_x0020_Number"] = SerialNumber;
                                desrItem["Software_x0020_Version"] = SoftwareVersion;
                                desrItem["Revision_x0020_Level"] = RevisionLevel;
                                desrItem["System_x0020_Date"] = DateTime.Today.ToString();
                                desrItem["Modality"] = Modality;
                                desrItem["SystemType"] = SystemType;
                                desrItem["MCSS"] = currentUser;
                                desrItem["ControlPanelLayout"] = ControlPanelLayout;
                                desrItem["ModalityWorkListEmpty"] = ModalityWorkListEmpty;
                                desrItem["AllSoftwareLoadedAndFunctioning"] = AllSoftwareLoadedAndFunctioning;
                                desrItem["IfNoExplain"] = IfNoExplain;
                                desrItem["NPDPresetsOnSystem"] = NPDPresetsOnSystem;
                                desrItem["HDDFreeOfPatientStudies"] = HDDFreeOfPatientStudies;
                                desrItem["DemoImagesLoadedOnHardDrive"] = DemoImagesLoadedOnHardDrive;
                                desrItem["SystemPerformedAsExpected"] = SystemPerformedAsExpected;
                                desrItem["SystemPerformedNotAsExpectedExplain"] = SystemPerformedNotAsExpectedExplain;
                                desrItem["AnyIssuesDuringDemo"] = AnyIssuesDuringDemo;
                                desrItem["wasServiceContacted"] = wasServiceContacted;
                                desrItem["ConfirmModalityWorkListRemoved"] = ConfirmModalityWorkListRemoved;
                                desrItem["ConfirmSystemHDDEmptied"] = ConfirmSystemHDDEmptied;
                                desrItem["LayoutChangeExplain"] = LayoutChangeExplain;
                                desrItem["Comments"] = Comments;
                                desrItem["IsFinal"] = IsFinal;
                                desrItem["Author"] = currentUser;
                                desrItem["Editor"] = currentUser;
                                desrItem.Update();

                                id = desrItem["ID"] + "";
                                web.AllowUnsafeUpdates = false;


                                SPUser css = currentUser;

                                string SystemDate = desrItem["System_x0020_Date"].ToString();
                                SystemDate = ((SystemDate != null && SystemDate != "") ? Convert.ToDateTime(SystemDate).ToShortDateString() : "");


                                messageBody = "";

                                messageBody += "<html><head><style>body{font-size:12.0pt;font-family:'Calibri','sans-serif';}p{margin-right:0in;margin-left:0in;font-size:12.0pt;font-family:'Calibri','serif';}</style></head>";
                                messageBody += "<body ><div class=WordSection1>&nbsp;<table border=0 cellspacing=0 cellpadding=0 style='width:623;'> ";
                                messageBody += "<tr><td colspan=2 valign=top>  This is a system generated email to notify you about a demo equipment’s critical status.  </td></tr>";
                                messageBody += "<tr><td colspan=2 valign=top >  &nbsp;  </td></tr>";
                                messageBody += "<tr><td colspan=2 valign=top >  <b><u>System information</u></b>  </td></tr>";
                                messageBody += "<tr><td valign=top >  System type:  </td>  <td valign=top >" + desrItem["SystemType"] + "</td></tr>";
                                messageBody += "<tr><td valign=top >  System serial number:  </td>  <td valign=top >  " + SerialNumber + "  </td></tr>";
                                messageBody += "<tr><td valign=top >Software version:  </td>  <td valign=top > " + desrItem["Software_x0020_Version"] + "  </td></tr>";
                                messageBody += "<tr><td valign=top >  Revision Level:  </td>  <td valign=top >  " + desrItem["Revision_x0020_Level"] + "  </td></tr>";
                                messageBody += "<tr><td valign=top >  Date:  </td>  <td  valign=top >  " + SystemDate + "  </td></tr>";
                                messageBody += "<tr><td valign=top >  CSS:  </td>  <td valign=top >  " + css.Name + "  </td></tr>";
                                messageBody += "<tr><td valign=top >  Comments:  </td>  <td valign=top >  " + Comments + "  </td></tr>";
                                messageBody += "<tr><td valign=top >  &nbsp;  </td>  <td valign=top >  &nbsp;  </td></tr>";
                                messageBody += "<tr><td colspan=2 valign=top >  <b><u>System condition on arrival</u></b>  </td></tr>";
                                messageBody += "<tr><td valign=top >  Control panel layout:  </td>  <td valign=top >  " + ControlPanelLayout + "  </td></tr>";
                                messageBody += "<tr><td valign=top >  Explain if changed:  </td>  <td valign=top >  " + LayoutChangeExplain + "  </td></tr>";
                                messageBody += "<tr><td valign=top >  Modality work list empty:  </td>  <td valign=top >  " + ModalityWorkListEmpty + "  </td></tr>";
                                messageBody += "<tr><td valign=top >  All software loaded and functioning:  </td>  <td valign=top >  " + AllSoftwareLoadedAndFunctioning + "  </td></tr>";
                                messageBody += "<tr><td valign=top >  &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Please explain:  </td>  <td valign=top >  " + IfNoExplain + "  </td></tr>";
                                messageBody += "<tr><td valign=top >  NPD presets on system:  </td>  <td valign=top >  " + NPDPresetsOnSystem + "  </td></tr>";
                                messageBody += "<tr><td valign=top >  HDD free of patients studies:  </td>  <td valign=top >  " + HDDFreeOfPatientStudies + "  </td></tr>";
                                messageBody += "<tr><td valign=top >  Demo images loaded on hard drive:  </td>  <td valign=top >  " + DemoImagesLoadedOnHardDrive + "  </td></tr>";
                                messageBody += "<tr><td valign=top >  &nbsp;  </td>  <td valign=top >  &nbsp;  </td></tr>";
                                messageBody += "<tr><td colspan=2 valign=top >  <b><u>Before leaving customer site</u></b>  </td></tr>";
                                messageBody += "<tr><td valign=top >  System performed as expected:  </td>  <td valign=top >  " + SystemPerformedAsExpected + "  </td></tr>";
                                messageBody += "<tr><td valign=top >  &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Please explain:  </td>  <td valign=top >  " + SystemPerformedNotAsExpectedExplain + "  </td></tr>";
                                messageBody += "<tr><td valign=top>  Were any issues discovered with system during demo</span>:  </td>  <td valign=top>    " + AnyIssuesDuringDemo + "  </td></tr>";
                                messageBody += "<tr><td valign=top>  Was service contacted:  </td>  <td valign=top>    " + wasServiceContacted + "  </td></tr>";
                                messageBody += "<tr><td valign=top>  Confirm that you have removed modality work list from system::  </td>  </span>  <td valign=top>    " + ConfirmModalityWorkListRemoved + "  </td></tr>";
                                messageBody += "<tr><td valign=top>  Confirm that you have emptied system HDD emptied of all patient studies:  </td>  </span>  <td valign=top >    " + ConfirmSystemHDDEmptied + "  </td></tr>";
                                messageBody += "<tr><td valign=top >  &nbsp;  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                messageBody += "<tr><td valign=top >  <b><u>Specialist Information</u></b>  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                messageBody += "<tr><td valign=top >  " + currentUser.Name + "  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                messageBody += "<tr><td valign=top>  " + WorkPhone + "   </td>  <td valign=top >    &nbsp;  </td></tr>";
                                messageBody += "<tr><td valign=top >  " + currentUser.Email.ToLower() + "  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                messageBody += "<tr><td valign=top >  &nbsp;  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                messageBody += "<tr><td valign=top >  &nbsp;  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                messageBody += "</table></div></body></html>";

                                SPList emailsList = web.Lists["DESREmailRecepients"];
                                plannerEmail = "";
                                appManagersEmails = "";
                                foreach (SPListItem emailItem in emailsList.Items)
                                {
                                    if (Convert.ToString(emailItem["Title"]).ToLower() == "planner")
                                    {
                                        plannerEmail = Convert.ToString(emailItem["Email"]);
                                    }
                                    if (Convert.ToString(emailItem["Title"]).ToLower() == Modality.ToLower())
                                    {
                                        appManagersEmails += Convert.ToString(emailItem["Email"]) + ",";
                                    }
                                }

                                if (appManagersEmails.EndsWith(",") || appManagersEmails.EndsWith(";"))
                                    appManagersEmails = appManagersEmails.Substring(0, appManagersEmails.Length - 1);

                                if (ModalityWorkListEmpty == "No" ||
                                    AllSoftwareLoadedAndFunctioning == "No" ||
                                    NPDPresetsOnSystem == "No" ||
                                    HDDFreeOfPatientStudies == "No" ||
                                    DemoImagesLoadedOnHardDrive == "No" ||
                                    SystemPerformedAsExpected == "No" ||
                                    AnyIssuesDuringDemo == "Yes")
                                {
                                    isSendEmail = true;
                                    currentUserToken = currentUser.UserToken;
                                    messageSubject = "Demo Equipment Status Alert - " + SystemType + " - " + SerialNumber;
                                }
                            }
                        }
                    }
                }
            });

            if (isSendEmail && IsFinal.Equals("Yes", StringComparison.CurrentCultureIgnoreCase))
            {
                using (SPSite impsite = new SPSite(SPUrl, currentUserToken))
                {
                    using (SPWeb impweb = impsite.OpenWeb())
                    {
                        StringDictionary headers = new StringDictionary();
                        headers.Add("to", appManagersEmails);
                        headers.Add("cc", plannerEmail);
                        headers.Add("from", "portaladmin@tams.com");
                        headers.Add("subject", messageSubject);

                        SPUtility.SendEmail(impweb, headers, messageBody);
                    }
                }
            }

            this.AddLog(SPUrl, "ADD NEW", null, authInfo);
        }
        catch { }

        List<string> retValues = new List<string>();
        retValues.Add(id);

        return CreateJsonResponse(retValues.ToArray(), callback);
    }

    public class Catalog
    {
        public string Modality;
        public string Product;
        public string SystemType;
        public string Software_x0020_Version;
        public string Revision_x0020_Level;
        public string System_x0020_Date;
        public string MCSS;
        public string Serial_x0020_Number;
        public string Total_x0020_Quantity_x0020_Ordered;
        public string ID;
        public string ImageURL;
        public string Creator;
    }

    #endregion

    /*
    #region OP::GetUserInfo

    public string GetUserInfo(string SPUrl, string callback)
    {
        List<string> documents = new List<string>();
        using (SPSite site = new SPSite(SPUrl))
        {
            using (SPWeb web = site.OpenWeb())
            {
                documents.Add(web.CurrentUser.Name);
                documents.Add(web.CurrentUser.Email);

                
            }
        }
        return CreateJsonResponse(documents.ToArray(), callback);
    }

    #endregion
     */
   
    #region OP:DownloadFile

    public string DownloadFile(string fileURL)
    {
        try
        {
            string stream = null;
            using (SPSite site = new SPSite(System.Configuration.ConfigurationManager.AppSettings["DownloadedFilesSite"]))
            {
                using (SPWeb web = site.OpenWeb())
                {
                    
                    
                    SPFile file = web.GetFile(fileURL);
                    byte[] data = file.OpenBinary();
                    if (!System.IO.File.Exists(@"" + System.Configuration.ConfigurationManager.AppSettings["DownloadedFilesFolder"] + file.Name))
                    {
                        FileStream fs = new FileStream(@"" + System.Configuration.ConfigurationManager.AppSettings["DownloadedFilesFolder"] + file.Name, FileMode.Create, FileAccess.Write);
                        BinaryWriter w = new BinaryWriter(fs);
                        w.Write(data, 0, (int)file.Length);
                        w.Close();
                        fs.Close();
                    }
                    stream = file.Name;
                }
            }
            return stream;
        }
        catch (Exception ex) { return null; }
    }

    #endregion

    #region OP:GetSystemTypes
    public string GetSystemTypes(string SPUrl, string callback)
    {
        List<string> systemTypeList = new List<string>();
        try
        {
            SPSecurity.RunWithElevatedPrivileges(delegate()
            {
                using (SPSite site = new SPSite(SPUrl))
                {
                    using (SPWeb web = site.OpenWeb())
                    {
                        SPList mList = web.Lists["DESRSystems"];
                        SPQuery camlQuery = new SPQuery();
                        camlQuery.Query = @"<OrderBy>
                            <FieldRef Name='SystemType' />
                        </OrderBy>";
                        camlQuery.ViewFields = @"<FieldRef Name='SystemType' />";
                        SPListItemCollection listItems = mList.GetItems(camlQuery);
                        string systemType = "";
                        foreach (SPListItem item in listItems)
                        {
                            if (systemType != item["SystemType"].ToString())
                            {
                                systemType = item["SystemType"].ToString();
                                systemTypeList.Add(systemType);
                            }
                        }
                    }
                }
            });
        }
        catch { }
        return CreateJsonResponse(systemTypeList.ToArray(), callback);
    }
    #endregion

    #region OP:GetCPLValues

    public string GetCPLValues(string SPUrl, string callback)
    {
        List<string> choiceList = new List<string>();
        try
        {
            SPSecurity.RunWithElevatedPrivileges(delegate()
            {
                using (SPSite site = new SPSite(SPUrl))
                {
                    using (SPWeb web = site.OpenWeb())
                    {
                        SPList mList = web.Lists["DESR"];
                        SPFieldChoice mField = (SPFieldChoice)mList.Fields["ControlPanelLayout"];
                        foreach (string mChoice in mField.Choices)
                        {
                            choiceList.Add(mChoice);
                        }
                    }
                }
            });
        }
        catch { }
        return CreateJsonResponse(choiceList.ToArray(), callback);
    }
    #endregion

    #region OP:LogOut

    public void LogOut(string SPUrl, string authInfo)
    {
        this.AddLog(SPUrl, "LOGOUT", null, authInfo);
    }

    #endregion

    #region OP:AccessedHelp

    public void AccessedHelp(string SPUrl, string authInfo)
    {
        this.AddLog(SPUrl, "ACCESSED HELP", null, authInfo);
    }

    #endregion

    #region OP: GetHistoryStatuses

    public class StatusHistory
    {
        public string ID;
        public string Title;
        public string SerialNumber;
        public string SoftwareVersion;
        public string RevisionLevel;
        public string SystemDate;
        public string Modality;
        public string SystemType;
        public string MCSS;
        public string ControlPanelLayout;
        public string ModalityWorkListEmpty;
        public string AllSoftwareLoadedAndFunctioning;
        public string IfNoExplain;
        public string NPDPresetsOnSystem;
        public string HDDFreeOfPatientStudies;
        public string DemoImagesLoadedOnHardDrive;
        public string SystemPerformedAsExpected;
        public string SystemPerformedNotAsExpectedExplain;
        public string AnyIssuesDuringDemo;
        public string wasServiceContacted;
        public string ConfirmModalityWorkListRemoved;
        public string ConfirmSystemHDDEmptied;
        public string LayoutChangeExplain;
        public string Comments;
        public string AdditionalComments;
        public string Modified;
        public string Created;
        public string CreatedBy;
        public string ModifiedBy;
        public string IsFinal;
    }

    public string GetHistoryStatuses(string SPUrl, string callback, string authInfo)
    {
        List<StatusHistory> historyItems = new List<StatusHistory>();
        try
        {
            SPSecurity.RunWithElevatedPrivileges(delegate()
            {
                using (SPSite site = new SPSite(SPUrl))
                {
                    using (SPWeb web = site.OpenWeb())
                    {

                        string loginString = decodeAuthentication(authInfo);
                        if (loginString.IndexOf(':') > 0)
                        {
                            web.AllowUnsafeUpdates = true;
                            SPUser currentUser = web.EnsureUser(loginString.Substring(0, loginString.IndexOf(':')));

                            if (currentUser != null)
                            {
                                SPList desrList = web.Lists["DESR"];

                                SPQuery camlQuery = new SPQuery();
                                camlQuery.Query = @"<Where><Eq><FieldRef Name='Author' LookupId='TRUE' /><Value Type='Integer'>" + currentUser.ID + @"</Value></Eq></Where><OrderBy><FieldRef Name='Created' Ascending='False' /></OrderBy>";

                                SPListItemCollection listItems = desrList.GetItems(camlQuery);
                                foreach (SPListItem item in listItems)
                                {
                                    StatusHistory his = new StatusHistory
                                    {
                                        ID = item.ID.ToString(),
                                        Title = GetSPValue(item["Title"]),
                                        SerialNumber = GetSPValue(item["Serial_x0020_Number"]),
                                        SoftwareVersion = GetSPValue(item["Software_x0020_Version"]),
                                        RevisionLevel = GetSPValue(item["Revision_x0020_Level"]),
                                        SystemDate = GetSPValue(item["System_x0020_Date"]),
                                        Modality = GetSPValue(item["Modality"]),
                                        SystemType = GetSPValue(item["SystemType"]),
                                        MCSS = GetSPValue(item["MCSS"]).Substring(GetSPValue(item["MCSS"]).IndexOf('#') + 1),
                                        ControlPanelLayout = GetSPValue(item["ControlPanelLayout"]),
                                        ModalityWorkListEmpty = GetSPValue(item["ModalityWorkListEmpty"]),
                                        AllSoftwareLoadedAndFunctioning = GetSPValue(item["AllSoftwareLoadedAndFunctioning"]),
                                        IfNoExplain = GetSPValue(item["IfNoExplain"]),
                                        NPDPresetsOnSystem = GetSPValue(item["NPDPresetsOnSystem"]),
                                        HDDFreeOfPatientStudies = GetSPValue(item["HDDFreeOfPatientStudies"]),
                                        DemoImagesLoadedOnHardDrive = GetSPValue(item["DemoImagesLoadedOnHardDrive"]),
                                        SystemPerformedAsExpected = GetSPValue(item["SystemPerformedAsExpected"]),
                                        SystemPerformedNotAsExpectedExplain = GetSPValue(item["SystemPerformedNotAsExpectedExplain"]),
                                        AnyIssuesDuringDemo = GetSPValue(item["AnyIssuesDuringDemo"]),
                                        wasServiceContacted = GetSPValue(item["wasServiceContacted"]),
                                        ConfirmModalityWorkListRemoved = GetSPValue(item["ConfirmModalityWorkListRemoved"]),
                                        ConfirmSystemHDDEmptied = GetSPValue(item["ConfirmSystemHDDEmptied"]),
                                        LayoutChangeExplain = GetSPValue(item["LayoutChangeExplain"]),
                                        Comments = GetSPValue(item["Comments"]),
                                        AdditionalComments = GetSPValue(item["AdditionalComments"]),
                                        Modified = GetSPValue(item["Modified"]),
                                        Created = GetSPValue(item["Created"]),
                                        CreatedBy = GetSPValue(item["Author"]),
                                        ModifiedBy = GetSPValue(item["Editor"]),
                                        IsFinal = GetSPValue(item["IsFinal"])
                                    };
                                    historyItems.Add(his);
                                }
                            }
                        }
                    }
                }
            });
        }
        catch { }

        return CreateJsonResponse(historyItems.ToArray(), callback);
    }

    public string GetHistoryStatusById(string SPUrl, string statusId, string callback, string authInfo)
    {
        List<StatusHistory> historyItems = new List<StatusHistory>();
        try
        {
            SPSecurity.RunWithElevatedPrivileges(delegate()
            {
                using (SPSite site = new SPSite(SPUrl))
                {
                    using (SPWeb web = site.OpenWeb())
                    {

                        string loginString = decodeAuthentication(authInfo);
                        if (loginString.IndexOf(':') > 0)
                        {
                            web.AllowUnsafeUpdates = true;
                            SPUser currentUser = web.EnsureUser(loginString.Substring(0, loginString.IndexOf(':')));

                            if (currentUser != null)
                            {
                                SPList desrList = web.Lists["DESR"];
                                SPListItem item = desrList.GetItemById(int.Parse(statusId));

                                if (item != null && (new SPFieldUserValue(web, item[SPBuiltInFieldId.Author].ToString())).User.ID == currentUser.ID)
                                {
                                    StatusHistory his = new StatusHistory
                                    {
                                        ID = item.ID.ToString(),
                                        Title = GetSPValue(item["Title"]),
                                        SerialNumber = GetSPValue(item["Serial_x0020_Number"]),
                                        SoftwareVersion = GetSPValue(item["Software_x0020_Version"]),
                                        RevisionLevel = GetSPValue(item["Revision_x0020_Level"]),
                                        SystemDate = GetSPValue(item["System_x0020_Date"]),
                                        Modality = GetSPValue(item["Modality"]),
                                        SystemType = GetSPValue(item["SystemType"]),
                                        MCSS = GetSPValue(item["MCSS"]).Substring(GetSPValue(item["MCSS"]).IndexOf('#') + 1),
                                        ControlPanelLayout = GetSPValue(item["ControlPanelLayout"]),
                                        ModalityWorkListEmpty = GetSPValue(item["ModalityWorkListEmpty"]),
                                        AllSoftwareLoadedAndFunctioning = GetSPValue(item["AllSoftwareLoadedAndFunctioning"]),
                                        IfNoExplain = GetSPValue(item["IfNoExplain"]),
                                        NPDPresetsOnSystem = GetSPValue(item["NPDPresetsOnSystem"]),
                                        HDDFreeOfPatientStudies = GetSPValue(item["HDDFreeOfPatientStudies"]),
                                        DemoImagesLoadedOnHardDrive = GetSPValue(item["DemoImagesLoadedOnHardDrive"]),
                                        SystemPerformedAsExpected = GetSPValue(item["SystemPerformedAsExpected"]),
                                        SystemPerformedNotAsExpectedExplain = GetSPValue(item["SystemPerformedNotAsExpectedExplain"]),
                                        AnyIssuesDuringDemo = GetSPValue(item["AnyIssuesDuringDemo"]),
                                        wasServiceContacted = GetSPValue(item["wasServiceContacted"]),
                                        ConfirmModalityWorkListRemoved = GetSPValue(item["ConfirmModalityWorkListRemoved"]),
                                        ConfirmSystemHDDEmptied = GetSPValue(item["ConfirmSystemHDDEmptied"]),
                                        LayoutChangeExplain = GetSPValue(item["LayoutChangeExplain"]),
                                        Comments = GetSPValue(item["Comments"]),
                                        AdditionalComments = GetSPValue(item["AdditionalComments"]),
                                        Modified = GetSPValue(item["Modified"]),
                                        Created = GetSPValue(item["Created"]),
                                        CreatedBy = GetSPValue(item["Author"]),
                                        ModifiedBy = GetSPValue(item["Editor"]),
                                        IsFinal = GetSPValue(item["IsFinal"])
                                    };
                                    historyItems.Add(his);
                                }
                            }
                        }
                    }
                }
            });
        }
        catch { }

        return CreateJsonResponse(historyItems.ToArray(), callback);
    }

    private string GetSPValue(object obj)
    {
        if (obj != null)
            return obj.ToString();
        else
            return string.Empty;
    }

    #endregion

    #region OP:AddAdditionalComments

    public string AddAdditionalComments(string SPUrl, int itemid, string comment, string callback, string authInfo)
    {
        List<int> actionResultes = new List<int>();
        try
        {
            SPSecurity.RunWithElevatedPrivileges(delegate()
            {
                using (SPSite site = new SPSite(SPUrl))
                {
                    using (SPWeb web = site.OpenWeb())
                    {
                        string loginString = decodeAuthentication(authInfo);
                        if (loginString.IndexOf(':') > 0)
                        {
                            web.AllowUnsafeUpdates = true;
                            SPUser currentUser = web.EnsureUser(loginString.Substring(0, loginString.IndexOf(':')));

                            if (currentUser != null)
                            {
                                SPList desrList = web.Lists["DESR"];
                                SPListItem item = desrList.GetItemById(itemid);
                                if (item != null)
                                {
                                    web.AllowUnsafeUpdates = true;

                                    //update desrsystem list
                                    item["AdditionalComments"] = GetSPValue(item["AdditionalComments"]) + "<b>" + DateTime.Now.ToString() + " " + currentUser.Name + ": </b>" + comment + "<br />";
                                    item.Update();
                                    web.AllowUnsafeUpdates = false;

                                    actionResultes.Add(itemid);
                                }
                            }
                        }
                    }
                }
            });

            this.AddLog(SPUrl, "ADD STATUS ADDITIONAL COMMENT", null, authInfo);
        }
        catch { }
        
        return CreateJsonResponse(actionResultes.ToArray(), callback);
    }

    #endregion

    #region OP:AddLog

    public void AddLog(string SPUrl, string action, string searchText, string authInfo)
    {
        string currentUser = "";
        SPSecurity.RunWithElevatedPrivileges(delegate()
        {
            using (SPSite site = new SPSite(SPUrl))
            {
                using (SPWeb web = site.OpenWeb())
                {
                    string loginString = decodeAuthentication(authInfo);
                    if (loginString.IndexOf(':') > 0)
                    {
                        web.AllowUnsafeUpdates = true;
                        SPUser currentSPUser = web.EnsureUser(loginString.Substring(0, loginString.IndexOf(':')));

                        if (currentSPUser != null)
                        {
                            currentUser = currentSPUser.LoginName.Substring(currentSPUser.LoginName.IndexOf('|') + 1);
                        }
                    }
                }
            }
        });

        if (!string.IsNullOrEmpty(currentUser))
        {
            using (SqlConnection sqlConn = new SqlConnection(System.Configuration.ConfigurationManager.AppSettings["SQLConnection"]))
            {
                using (SqlCommand sqlComm = new SqlCommand())
                {
                    sqlComm.Connection = sqlConn;
                    sqlComm.CommandText = "dbo.sp_addDESRLog";
                    sqlComm.CommandType = CommandType.StoredProcedure;

                    SqlParameter username = sqlComm.CreateParameter();
                    username.ParameterName = "@username";
                    username.DbType = DbType.String;
                    username.Value = currentUser;
                    sqlComm.Parameters.Add(username);

                    SqlParameter useraction = sqlComm.CreateParameter();
                    useraction.ParameterName = "@action";
                    useraction.DbType = DbType.String;
                    useraction.Value = action;
                    sqlComm.Parameters.Add(useraction);

                    SqlParameter userSearchText = sqlComm.CreateParameter();
                    userSearchText.ParameterName = "@searchText";
                    userSearchText.DbType = DbType.String;
                    userSearchText.Value = DBNull.Value;
                    if (searchText != null)
                    {
                        userSearchText.Value = searchText;
                    }
                    sqlComm.Parameters.Add(userSearchText);

                    try
                    {
                        sqlConn.Open();
                        sqlComm.ExecuteScalar();
                    }
                    catch (Exception ex)
                    {
                        throw ex;
                        //throw new Exception(ex.Message);
                    }
                }
            }
        }
    }

    #endregion

    #region Test Email
    public string TestEmail(string emailto, string spurl)
    {
        bool retval = false;
        SPUserToken curentUserToken = null;

        SPSecurity.RunWithElevatedPrivileges(delegate()
        {
            using (SPSite site = new SPSite(spurl))
            {
                using (SPWeb web = site.OpenWeb())
                {
                    web.AllowUnsafeUpdates = true;
                    SPUser currentUser = web.EnsureUser("tamsdomain\\kho");
                    curentUserToken = currentUser.UserToken;

                    StringDictionary headers = new StringDictionary();
                    headers.Add("to", emailto);
                    headers.Add("cc", "tmehta@tusspdev1.tams.com");
                    headers.Add("from", "portaladmin@tams.com");
                    headers.Add("subject", "Demo Equipment Status Alert 1");


                    retval = SPUtility.SendEmail(web, headers, "Testing email message 1");
                }
            }
        });

        using (SPSite impsite = new SPSite(spurl, curentUserToken))
        {
            using (SPWeb impweb = impsite.OpenWeb())
            {
                StringDictionary headers = new StringDictionary();
                headers.Add("to", emailto);
                headers.Add("cc", "tmehta@tusspdev1.tams.com");
                headers.Add("from", "portaladmin@tams.com");
                headers.Add("subject", "Demo Equipment Status Alert 2");


                retval = SPUtility.SendEmail(impweb, headers, "Testing email message 2");
            }
        }

        return CreateJsonResponse(retval);
    }
    #endregion

}