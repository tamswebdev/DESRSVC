﻿using System;
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

    private string longitude = "";
    private string latitude = "";

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

            longitude = Request.Params["lon"];
            latitude = Request.Params["lat"];

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
        Response.ContentType = "application/javascript; charset=utf-8";
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
	
	public void WriteLog(string logMessage)
	{
		using (var logFile = System.IO.File.AppendText(@"C:\desr_log.txt"))
		{
			logFile.WriteLine(DateTime.Now);
			logFile.WriteLine(logMessage);
			logFile.WriteLine();
		}
	}


    private string GenerateEmailContent(SPListItem desrItem, SPUser currentUser, string WorkPhone)
    {
        string messageBody = "";

        string SystemDate = desrItem["System_x0020_Date"].ToString();
        SystemDate = ((SystemDate != null && SystemDate != "") ? Convert.ToDateTime(SystemDate).ToShortDateString() : "");

        messageBody += "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Strict//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd\"><html><head>" + Environment.NewLine;
        messageBody += "<style>body{font-size:12.0pt;font-family:'Calibri','sans-serif';}p{margin-right:0in;margin-left:0in;font-size:12.0pt;font-family:'Calibri','serif';}.app-manager{background-color: yellow;}.app-planner{color:red;}.app-both{background-color:yellow;color:red;}.msgIndent{padding-left: 25px;}</style></head>" + Environment.NewLine;
        messageBody += "<body >" + Environment.NewLine;
        messageBody += "<div><img alt=\"\" src=\"http://tams-media.com/DESR/DESR%20masthead%20710x71.png\" /></div>" + Environment.NewLine;
        messageBody += "<div class=WordSection1>&nbsp;<table border=0 cellspacing=0 cellpadding=0 style='width:623;'> " + Environment.NewLine;
        messageBody += "<tr><td colspan=2 valign=top>  This is a system generated email to notify you about a demo equipment’s critical status.  </td></tr>" + Environment.NewLine;
        messageBody += "<tr><td colspan=2 valign=top >&nbsp;</td></tr>" + Environment.NewLine;
        messageBody += "<tr><td colspan=2 valign=top >  <b><u>System information</u></b>  </td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top >  System type:  </td><td valign=top >" + desrItem["SystemType"] + "</td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top >  System serial number:  </td><td valign=top >  " + desrItem["Serial_x0020_Number"] + "  </td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top >Software version:  </td><td valign=top > " + desrItem["Software_x0020_Version"] + "  </td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top >  Revision Level:  </td><td valign=top >  " + desrItem["Revision_x0020_Level"] + "  </td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top >  Date:  </td><td  valign=top >  " + SystemDate + "  </td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top >  CSS:  </td><td valign=top >  " + currentUser.Name + "  </td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top >  Comments:  </td><td valign=top >  " + desrItem["Comments"] + "  </td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top >&nbsp;</td><td valign=top >&nbsp;</td></tr>" + Environment.NewLine;
        messageBody += "<tr><td colspan=2 valign=top >  <b><u>System condition on arrival</u></b>  </td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top >  Control panel layout:  </td><td valign=top >  " + EmailHighlight(desrItem["ControlPanelLayout"], "ControlPanelLayout") + "  </td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top class=msgIndent>  Explain if changed:  </td><td valign=top >  " + desrItem["LayoutChangeExplain"] + "  </td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top >  Modality work list empty:  </td><td valign=top >  " + EmailHighlight(desrItem["ModalityWorkListEmpty"], "ModalityWorkListEmpty") + "  </td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top >  All software loaded and functioning:  </td><td valign=top >  " + EmailHighlight(desrItem["AllSoftwareLoadedAndFunctioning"], "AllSoftwareLoadedAndFunctioning") + "  </td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top class=msgIndent>  Please explain:  </td><td valign=top >  " + desrItem["IfNoExplain"] + "  </td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top >  NPD presets on system:  </td><td valign=top >  " + EmailHighlight(desrItem["NPDPresetsOnSystem"], "NPDPresetsOnSystem") + "  </td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top >  HDD free of patients studies:  </td><td valign=top >  " + EmailHighlight(desrItem["HDDFreeOfPatientStudies"], "HDDFreeOfPatientStudies") + "  </td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top >  Demo images loaded on hard drive:  </td><td valign=top >  " + EmailHighlight(desrItem["DemoImagesLoadedOnHardDrive"], "DemoImagesLoadedOnHardDrive") + "  </td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top >&nbsp;</td><td valign=top >&nbsp;</td></tr>" + Environment.NewLine;
        messageBody += "<tr><td colspan=2 valign=top >  <b><u>Before leaving customer site</u></b>  </td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top >  System performed as expected:  </td><td valign=top >  " + EmailHighlight(desrItem["SystemPerformedAsExpected"], "SystemPerformedAsExpected") + "  </td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top  class=msgIndent>  Please explain:  </td><td valign=top >  " + desrItem["SystemPerformedNotAsExpectedExplain"] + "  </td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top>  Were any issues discovered with system during demo:  </td><td valign=top>    " + EmailHighlight(desrItem["AnyIssuesDuringDemo"], "AnyIssuesDuringDemo") + "  </td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top>  Was service contacted:  </td><td valign=top>    " + EmailHighlight(desrItem["wasServiceContacted"], "wasServiceContacted") + "  </td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top>  Confirm that you have removed modality work list from system:  </td><td valign=top>    " + EmailHighlight(desrItem["ConfirmModalityWorkListRemoved"], "ConfirmModalityWorkListRemoved") + "  </td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top>  Confirm that you have emptied system HDD emptied of all patient studies:  </td><td valign=top >    " + EmailHighlight(desrItem["ConfirmSystemHDDEmptied"], "ConfirmSystemHDDEmptied") + "  </td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top >&nbsp;</td><td valign=top >&nbsp;</td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top >  <b><u>Additional Comments</u></b>  </td><td valign=top >&nbsp;</td></tr>" + Environment.NewLine;
        messageBody += "<tr><td colspan=2 valign=top >  " + desrItem["AdditionalComments"] + "  </td><td valign=top >&nbsp;</td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top >&nbsp;</td><td valign=top >  &nbsp;</td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top >  <b><u>Specialist Information</u></b>  </td><td valign=top >&nbsp;</td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top >  " + currentUser.Name + "  </td><td valign=top >&nbsp;</td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top>  " + WorkPhone + "   </td><td valign=top >&nbsp;</td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top >  " + currentUser.Email.ToLower() + "  </td><td valign=top >&nbsp;</td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top >&nbsp;</td><td valign=top >&nbsp;</td></tr>" + Environment.NewLine;
        messageBody += "<tr><td valign=top >&nbsp;</td><td valign=top >&nbsp;</td></tr>" + Environment.NewLine;
        messageBody += "</table></div></body></html>";

        return messageBody;
    }

    //EmailHighlight(, "")
    private string EmailHighlight(object spvalue, string checkingKey)
    {
        string value = ((spvalue != null && spvalue != "") ? spvalue.ToString() : "");

        switch (checkingKey + "--" + value.ToLower())
        {
            case "ControlPanelLayout--control panel changed":
                return "<span class='app-planner'>" + value + "</span>";
            case "ModalityWorkListEmpty--no":
                return "<span class='app-manager'>" + value + "</span>";
            case "AllSoftwareLoadedAndFunctioning--no":
                return "<span class='app-planner'>" + value + "</span>";
            case "NPDPresetsOnSystem--no":
                return "<span class='app-both'>" + value + "</span>";
            case "HDDFreeOfPatientStudies--no":
                return "<span class='app-manager'>" + value + "</span>";
            case "DemoImagesLoadedOnHardDrive--no":
                return "<span class='app-manager'>" + value + "</span>";
            case "SystemPerformedAsExpected--no":
                return "<span class='app-planner'>" + value + "</span>";
            case "AnyIssuesDuringDemo--yes":
                return "<span class='app-planner'>" + value + "</span>";
            case "wasServiceContacted--no":
                return "" + value + "";
            case "ConfirmModalityWorkListRemoved--no":
                return "<span class='app-manager'>" + value + "</span>";
            case "ConfirmSystemHDDEmptied--no":
                return "<span class='app-manager'>" + value + "</span>";
            default:
                return value;
        }
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

    public string Authenticate(string authInfo, string currentURL, string SPUrl, string deviceInfo, string callback)
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


                    var url = System.Configuration.ConfigurationManager.AppSettings["GetUserInfoURL"].ToString().Replace("&amp;", "&").Replace("[EMAILADDRESS]", loginInfo.email);
                    var syncClient = new WebClient();
                    var content = syncClient.DownloadString(url);

                    string[] values = content.Split(",".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
                    foreach (string str in values)
                    {
                        if (str.StartsWith("\"WorkPhone\":", StringComparison.CurrentCultureIgnoreCase))
                            loginInfo.phone = str.ToLower().Replace("\"", "").Replace("workphone", "").Replace(":", "");
                    }

                    this.AddLog(SPUrl, "LOGIN", null, authInfo, deviceInfo);
                }
                else
                {
                    this.AddLog(SPUrl, "LOGIN-FAILED", null, authInfo, deviceInfo);
                }
            }
        }
        catch (Exception ex)
        {
            this.AddLog(SPUrl, "LOGIN-EXCEPTION", null, authInfo, deviceInfo);
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

    public string SearchCatalogs(string SPUrl, string searchText, string modality, string documentType, string callback, string authInfo, string deviceInfo)
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

            this.AddLog(SPUrl, "SEARCH", searchText, authInfo, deviceInfo);
        }
        catch {
            this.AddLog(SPUrl, "SEARCH-EXCEPTION", searchText, authInfo, deviceInfo);
        }
        
        
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
        string SystemPerformedNotAsExpectedExplain, string IsFinal, string authInfo, string callback, string statusId, string deviceInfo)
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
            bool isNew = true;
            bool isManual = false;

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
                                else
                                    isNew = false;

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
                                isManual = (desrItem["IsManual"] != null && desrItem["IsManual"].ToString().ToLower().Equals("yes") ? true : false);

                                web.AllowUnsafeUpdates = false;
                                SPUser css = currentUser;


                                string SystemDate = item["System_x0020_Date"].ToString();
                                SystemDate = ((SystemDate != null && SystemDate != "") ? Convert.ToDateTime(SystemDate).ToShortDateString() : "");
                                messageBody = GenerateEmailContent(desrItem, currentUser, WorkPhone);

                                //messageBody += "<html><head><style>body{font-size:12.0pt;font-family:'Calibri','sans-serif';}p{margin-right:0in;margin-left:0in;font-size:12.0pt;font-family:'Calibri','serif';}</style></head><body >";
                                //messageBody += "<div><img alt=\"\" src=\"http://tams-media.com/DESR/DESR%20masthead%20710x71.png\" /></div>";
                                //messageBody += "<div class=WordSection1>&nbsp;<table border=0 cellspacing=0 cellpadding=0 style='width:623;'> ";
                                //messageBody += "<tr><td colspan=2 valign=top>  This is a system generated email to notify you about a demo equipment’s critical status.  </td></tr>";
                                //messageBody += "<tr><td colspan=2 valign=top >  &nbsp;  </td></tr>";
                                //messageBody += "<tr><tdcolspan=2 valign=top >  <b><u>System information</u></b>  </td></tr>";
                                //messageBody += "<tr><td valign=top >  System type:  </td>  <td valign=top >" + item["SystemType"] + "</td> </tr>";
                                //messageBody += "<tr><td valign=top >  System serial number:  </td>  <td valign=top >  " + item["Title"] + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >Software version:  </td>  <td valign=top > " + item["Software_x0020_Version"] + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >  Revision Level:  </td>  <td valign=top >  " + item["Revision_x0020_Level"] + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >  Date:  </td>  <td  valign=top >  " + SystemDate + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >  CSS:  </td>  <td valign=top >  " + css.Name + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >  Comments:  </td>  <td valign=top >  " + Comments + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >  &nbsp;  </td>  <td valign=top >  &nbsp;  </td></tr>";
                                //messageBody += "<tr><td colspan=2 valign=top >  <b><u>System condition on arrival</u></b>  </td></tr>";
                                //messageBody += "<tr><td valign=top >  Control panel layout:  </td>  <td valign=top >  " + ControlPanelLayout + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >  Explain if changed:  </td>  <td valign=top >  " + LayoutChangeExplain + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >  Modality work list empty:  </td>  <td valign=top >  " + ModalityWorkListEmpty + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >  All software loaded and functioning:  </td>  <td valign=top >  " + AllSoftwareLoadedAndFunctioning + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >  &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Please explain:  </td>  <td valign=top >  " + IfNoExplain + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >  NPD presets on system:  </td>  <td valign=top >  " + NPDPresetsOnSystem + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >  HDD free of patients studies:  </td>  <td valign=top >  " + HDDFreeOfPatientStudies + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >  Demo images loaded on hard drive:  </td>  <td valign=top >  " + DemoImagesLoadedOnHardDrive + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >  &nbsp;  </td>  <td valign=top >  &nbsp;  </td></tr>";
                                //messageBody += "<tr><td colspan=2 valign=top >  <b><u>Before leaving customer site</u></b>  </td></tr>";
                                //messageBody += "<tr><td valign=top >  System performed as expected:  </td>  <td valign=top >  " + SystemPerformedAsExpected + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >  &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Please explain:  </td>  <td valign=top >  " + SystemPerformedNotAsExpectedExplain + "  </td></tr>";
                                //messageBody += "<tr><td valign=top>  Were any issues discovered with system during demo</span>:  </td>  <td valign=top>    " + AnyIssuesDuringDemo + "  </td></tr>";
                                //messageBody += "<tr><td valign=top>  Was service contacted:  </td>  <td valign=top>    " + wasServiceContacted + "  </td></tr>";
                                //messageBody += "<tr><td valign=top>  Confirm that you have removed modality work list from system:  </td>  </span>  <td valign=top>    " + ConfirmModalityWorkListRemoved + "  </td></tr>";
                                //messageBody += "<tr><td valign=top>  Confirm that you have emptied system HDD emptied of all patient studies:  </td>  </span>  <td valign=top >    " + ConfirmSystemHDDEmptied + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >  &nbsp;  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                //messageBody += "<tr><td valign=top >  <b><u>Additional Comments</u></b>  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                //messageBody += "<tr><td colspan=2 valign=top >  " + item["AdditionalComments"] + "  </td>  <td valign=top >    &nbsp;  </td></tr>";

                                //messageBody += "<tr><td valign=top >  &nbsp;  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                //messageBody += "<tr><td valign=top >  <b><u>Specialist Information</u></b>  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                //messageBody += "<tr><td valign=top >  " + currentUser.Name + "  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                //messageBody += "<tr><td valign=top>  " + WorkPhone + "   </td>  <td valign=top >    &nbsp;  </td></tr>";
                                //messageBody += "<tr><td valign=top >  " + currentUser.Email.ToLower() + "  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                //messageBody += "<tr><td valign=top >  &nbsp;  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                //messageBody += "<tr><td valign=top >  &nbsp;  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                //messageBody += "</table></div></body></html>";

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
                       

                        //Send notice to planner for manually adding
                        if (isManual)
                        {
                            StringDictionary headers2 = new StringDictionary();
                            headers2.Add("to", plannerEmail);
                            headers2.Add("from", "portaladmin@tams.com");
                            headers2.Add("subject", "Manually Adding - " + messageSubject);

                            SPUtility.SendEmail(impweb, headers2, messageBody);
                        }
                    }
                }
            }

            this.AddLog(SPUrl, "ADD-" + (IsFinal.ToLower().Equals("yes") ? "FINAL":"DRAFT") + "-STATUS", null, authInfo, deviceInfo);
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
        string LayoutChangeExplain, string Comments, string WorkPhone, string SystemPerformedNotAsExpectedExplain, string authInfo, string callback, string IsFinal, string statusId, string deviceInfo)
    {
        string id = null;
        bool isNew = true;

        try
        {
            WorkPhone = WorkPhone.Substring(0, 3) + "-" + WorkPhone.Substring(3, 3) + "-" + WorkPhone.Substring(6);

            bool isSendEmail = false;
            SPUserToken currentUserToken = null;
            string plannerEmail = "";
            string appManagersEmails = "";
            string messageSubject = "";
            string messageBody = "";
            bool isManual = false;

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
                                else
                                    isNew = false;

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
                                if (isNew)
                                    desrItem["IsManual"] = "Yes";

                                desrItem["Author"] = currentUser;
                                desrItem["Editor"] = currentUser;
                                desrItem.Update();

                                id = desrItem["ID"] + "";
                                isManual = (desrItem["IsManual"] != null && desrItem["IsManual"].ToString().ToLower().Equals("yes") ? true : false);


                                web.AllowUnsafeUpdates = false;


                                SPUser css = currentUser;

                                string SystemDate = desrItem["System_x0020_Date"].ToString();
                                SystemDate = ((SystemDate != null && SystemDate != "") ? Convert.ToDateTime(SystemDate).ToShortDateString() : "");


                                messageBody = GenerateEmailContent(desrItem, currentUser, WorkPhone);

                                //messageBody += "<html><head><style>body{font-size:12.0pt;font-family:'Calibri','sans-serif';}p{margin-right:0in;margin-left:0in;font-size:12.0pt;font-family:'Calibri','serif';}</style></head>";
                                //messageBody += "<body >";
                                //messageBody += "<div><img alt=\"\" src=\"http://tams-media.com/DESR/DESR%20masthead%20710x71.png\" /></div>";
                                //messageBody += "<div class=WordSection1>&nbsp;<table border=0 cellspacing=0 cellpadding=0 style='width:623;'> ";
                                //messageBody += "<tr><td colspan=2 valign=top>  This is a system generated email to notify you about a demo equipment’s critical status.  </td></tr>";
                                //messageBody += "<tr><td colspan=2 valign=top >  &nbsp;  </td></tr>";
                                //messageBody += "<tr><td colspan=2 valign=top >  <b><u>System information</u></b>  </td></tr>";
                                //messageBody += "<tr><td valign=top >  System type:  </td>  <td valign=top >" + desrItem["SystemType"] + "</td></tr>";
                                //messageBody += "<tr><td valign=top >  System serial number:  </td>  <td valign=top >  " + SerialNumber + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >Software version:  </td>  <td valign=top > " + desrItem["Software_x0020_Version"] + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >  Revision Level:  </td>  <td valign=top >  " + desrItem["Revision_x0020_Level"] + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >  Date:  </td>  <td  valign=top >  " + SystemDate + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >  CSS:  </td>  <td valign=top >  " + css.Name + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >  Comments:  </td>  <td valign=top >  " + Comments + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >  &nbsp;  </td>  <td valign=top >  &nbsp;  </td></tr>";
                                //messageBody += "<tr><td colspan=2 valign=top >  <b><u>System condition on arrival</u></b>  </td></tr>";
                                //messageBody += "<tr><td valign=top >  Control panel layout:  </td>  <td valign=top >  " + ControlPanelLayout + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >  Explain if changed:  </td>  <td valign=top >  " + LayoutChangeExplain + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >  Modality work list empty:  </td>  <td valign=top >  " + ModalityWorkListEmpty + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >  All software loaded and functioning:  </td>  <td valign=top >  " + AllSoftwareLoadedAndFunctioning + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >  &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Please explain:  </td>  <td valign=top >  " + IfNoExplain + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >  NPD presets on system:  </td>  <td valign=top >  " + NPDPresetsOnSystem + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >  HDD free of patients studies:  </td>  <td valign=top >  " + HDDFreeOfPatientStudies + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >  Demo images loaded on hard drive:  </td>  <td valign=top >  " + DemoImagesLoadedOnHardDrive + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >  &nbsp;  </td>  <td valign=top >  &nbsp;  </td></tr>";
                                //messageBody += "<tr><td colspan=2 valign=top >  <b><u>Before leaving customer site</u></b>  </td></tr>";
                                //messageBody += "<tr><td valign=top >  System performed as expected:  </td>  <td valign=top >  " + SystemPerformedAsExpected + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >  &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Please explain:  </td>  <td valign=top >  " + SystemPerformedNotAsExpectedExplain + "  </td></tr>";
                                //messageBody += "<tr><td valign=top>  Were any issues discovered with system during demo</span>:  </td>  <td valign=top>    " + AnyIssuesDuringDemo + "  </td></tr>";
                                //messageBody += "<tr><td valign=top>  Was service contacted:  </td>  <td valign=top>    " + wasServiceContacted + "  </td></tr>";
                                //messageBody += "<tr><td valign=top>  Confirm that you have removed modality work list from system::  </td>  </span>  <td valign=top>    " + ConfirmModalityWorkListRemoved + "  </td></tr>";
                                //messageBody += "<tr><td valign=top>  Confirm that you have emptied system HDD emptied of all patient studies:  </td>  </span>  <td valign=top >    " + ConfirmSystemHDDEmptied + "  </td></tr>";
                                //messageBody += "<tr><td valign=top >  &nbsp;  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                //messageBody += "<tr><td valign=top >  <b><u>Additional Comments</u></b>  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                //messageBody += "<tr><td colspan=2 valign=top >  " + desrItem["AdditionalComments"] + "  </td>  <td valign=top >    &nbsp;  </td></tr>";

                                //messageBody += "<tr><td valign=top >  &nbsp;  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                //messageBody += "<tr><td valign=top >  <b><u>Specialist Information</u></b>  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                //messageBody += "<tr><td valign=top >  " + currentUser.Name + "  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                //messageBody += "<tr><td valign=top>  " + WorkPhone + "   </td>  <td valign=top >    &nbsp;  </td></tr>";
                                //messageBody += "<tr><td valign=top >  " + currentUser.Email.ToLower() + "  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                //messageBody += "<tr><td valign=top >  &nbsp;  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                //messageBody += "<tr><td valign=top >  &nbsp;  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                //messageBody += "</table></div></body></html>";

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

                        //Send notice to planner for manually adding
                        if (isManual)
                        {
                            StringDictionary headers2 = new StringDictionary();
                            headers2.Add("to", plannerEmail);
                            headers2.Add("from", "portaladmin@tams.com");
                            headers2.Add("subject", "Manually Adding - " + messageSubject);

                            SPUtility.SendEmail(impweb, headers2, messageBody);
                        }
                    }
                }
            }

            this.AddLog(SPUrl, (isNew ? "ADD-NEW-": "UPDATE-") + (IsFinal.ToLower().Equals("yes") ? "FINAL" : "DRAFT") + "-STATUS", null, authInfo, deviceInfo);
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
                        SPList mList = web.Lists["DESRCPLLookups"];
						SPQuery camlQuery = new SPQuery();
                        camlQuery.Query = @"<OrderBy>
                            <FieldRef Name='Order' />
                        </OrderBy>";
                        camlQuery.ViewFields = @"<FieldRef Name='Title' />";
                        SPListItemCollection listItems = mList.GetItems(camlQuery);
                        foreach (SPListItem item in listItems)
                        {
                            if (item["Title"] != null)
                            {
                                choiceList.Add(item["Title"].ToString());
                            }
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

    public void LogOut(string SPUrl, string authInfo, string deviceInfo)
    {
        this.AddLog(SPUrl, "LOGOUT", null, authInfo, deviceInfo);
    }

    #endregion

    #region OP:LogHomePage

    public void LogHomePage(string SPUrl, string authInfo, string deviceInfo)
    {
        this.AddLog(SPUrl, "PAGE-HOME", null, authInfo, deviceInfo);
    }

    #endregion

    #region OP:AccessedHelp

    public void AccessedHelp(string SPUrl, string authInfo, string deviceInfo)
    {
        this.AddLog(SPUrl, "ACCESS-HELP", null, authInfo, deviceInfo);
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

    public string GetHistoryStatuses(string SPUrl, string callback, string authInfo, string deviceInfo)
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

        this.AddLog(SPUrl, "VIEW-HISTORIES", null, authInfo, deviceInfo);
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

    public string AddAdditionalComments(string SPUrl, int itemid, string comment, string WorkPhone, string callback, string authInfo, string deviceInfo)
    {
        List<int> actionResultes = new List<int>();
        try
        {
            SPUserToken currentUserToken = null;
            string currentUserName = "";
            string plannerEmail = "";
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
                                SPListItem item = desrList.GetItemById(itemid);
                                if (item != null)
                                {
                                    web.AllowUnsafeUpdates = true;

                                    //update desrsystem list
                                    item["AdditionalComments"] = GetSPValue(item["AdditionalComments"]) + "<b>" + DateTime.Now.ToString() + " " + currentUser.Name + ": </b>" + comment + "<br />";
                                    item.Update();
                                    web.AllowUnsafeUpdates = false;

                                    actionResultes.Add(itemid);

                                    if (item["IsFinal"] != null && item["IsFinal"].ToString().Equals("Yes"))
                                    {
                                        messageBody = "";

                                        string SystemDate = item["System_x0020_Date"].ToString();
                                        SystemDate = ((SystemDate != null && SystemDate != "") ? Convert.ToDateTime(SystemDate).ToShortDateString() : "");

                                        if (WorkPhone.Length > 6)
                                            WorkPhone = WorkPhone.Substring(0, 3) + "-" + WorkPhone.Substring(3, 3) + "-" + WorkPhone.Substring(6);

                                        //string CSS = (item["MCSS"] != null ? item["MCSS"].ToString().Substring(item["MCSS"].ToString().IndexOf("#") + 1) : "");

                                        messageBody = GenerateEmailContent(item, currentUser, WorkPhone);

                                        /*
                                        messageBody += "<html><head><style>body{font-size:12.0pt;font-family:'Calibri','sans-serif';}p{margin-right:0in;margin-left:0in;font-size:12.0pt;font-family:'Calibri','serif';}</style></head>";
                                        messageBody += "<body >";
                                        messageBody += "<div><img alt=\"\" src=\"http://tams-media.com/DESR/DESR%20masthead%20710x71.png\" /></div>";
                                        messageBody += "<div class=WordSection1>&nbsp;<table border=0 cellspacing=0 cellpadding=0 style='width:623;'> ";
                                        messageBody += "<tr><td colspan=2 valign=top>  This is a system generated email to notify you about a demo equipment’s critical status.  </td></tr>";
                                        messageBody += "<tr><td colspan=2 valign=top >  &nbsp;  </td></tr>";
                                        messageBody += "<tr><td colspan=2 valign=top >  <b><u>System information</u></b>  </td></tr>";
                                        messageBody += "<tr><td valign=top >  System type:  </td>  <td valign=top >" + item["SystemType"] + "</td></tr>";
                                        messageBody += "<tr><td valign=top >  System serial number:  </td>  <td valign=top >  " + item["Serial_x0020_Number"] + "  </td></tr>";
                                        messageBody += "<tr><td valign=top >Software version:  </td>  <td valign=top > " + item["Software_x0020_Version"] + "  </td></tr>";
                                        messageBody += "<tr><td valign=top >  Revision Level:  </td>  <td valign=top >  " + item["Revision_x0020_Level"] + "  </td></tr>";
                                        messageBody += "<tr><td valign=top >  Date:  </td>  <td  valign=top >  " + SystemDate + "  </td></tr>";
                                        messageBody += "<tr><td valign=top >  CSS:  </td>  <td valign=top >  " + CSS + "  </td></tr>";
                                        messageBody += "<tr><td valign=top >  Comments:  </td>  <td valign=top >  " + item["Comments"] + "  </td></tr>";
                                        messageBody += "<tr><td valign=top >  &nbsp;  </td>  <td valign=top >  &nbsp;  </td></tr>";
                                        messageBody += "<tr><td colspan=2 valign=top >  <b><u>System condition on arrival</u></b>  </td></tr>";
                                        messageBody += "<tr><td valign=top >  Control panel layout:  </td>  <td valign=top >  " + item["ControlPanelLayout"] + "  </td></tr>";
                                        messageBody += "<tr><td valign=top >  Explain if changed:  </td>  <td valign=top >  " + item["LayoutChangeExplain"] + "  </td></tr>";
                                        messageBody += "<tr><td valign=top >  Modality work list empty:  </td>  <td valign=top >  " + item["ModalityWorkListEmpty"] + "  </td></tr>";
                                        messageBody += "<tr><td valign=top >  All software loaded and functioning:  </td>  <td valign=top >  " + item["AllSoftwareLoadedAndFunctioning"] + "  </td></tr>";
                                        messageBody += "<tr><td valign=top >  &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Please explain:  </td>  <td valign=top >  " + item["IfNoExplain"] + "  </td></tr>";
                                        messageBody += "<tr><td valign=top >  NPD presets on system:  </td>  <td valign=top >  " + item["NPDPresetsOnSystem"] + "  </td></tr>";
                                        messageBody += "<tr><td valign=top >  HDD free of patients studies:  </td>  <td valign=top >  " + item["HDDFreeOfPatientStudies"] + "  </td></tr>";
                                        messageBody += "<tr><td valign=top >  Demo images loaded on hard drive:  </td>  <td valign=top >  " + item["DemoImagesLoadedOnHardDrive"] + "  </td></tr>";
                                        messageBody += "<tr><td valign=top >  &nbsp;  </td>  <td valign=top >  &nbsp;  </td></tr>";
                                        messageBody += "<tr><td colspan=2 valign=top >  <b><u>Before leaving customer site</u></b>  </td></tr>";
                                        messageBody += "<tr><td valign=top >  System performed as expected:  </td>  <td valign=top >  " + item["SystemPerformedAsExpected"] + "  </td></tr>";
                                        messageBody += "<tr><td valign=top >  &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Please explain:  </td>  <td valign=top >  " + item["SystemPerformedNotAsExpectedExplain"] + "  </td></tr>";
                                        messageBody += "<tr><td valign=top>  Were any issues discovered with system during demo</span>:  </td>  <td valign=top>    " + item["AnyIssuesDuringDemo"] + "  </td></tr>";
                                        messageBody += "<tr><td valign=top>  Was service contacted:  </td>  <td valign=top>    " + item["wasServiceContacted"] + "  </td></tr>";
                                        messageBody += "<tr><td valign=top>  Confirm that you have removed modality work list from system::  </td>  </span>  <td valign=top>    " + item["ConfirmModalityWorkListRemoved"] + "  </td></tr>";
                                        messageBody += "<tr><td valign=top>  Confirm that you have emptied system HDD emptied of all patient studies:  </td>  </span>  <td valign=top >    " + item["ConfirmSystemHDDEmptied"] + "  </td></tr>";
                                        messageBody += "<tr><td valign=top >  &nbsp;  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                        messageBody += "<tr><td valign=top >  <b><u>Additional Comments</u></b>  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                        messageBody += "<tr><td colspan=2 valign=top >  " + item["AdditionalComments"] + "  </td>  <td valign=top >    &nbsp;  </td></tr>";

                                        messageBody += "<tr><td valign=top >  &nbsp;  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                        messageBody += "<tr><td valign=top >  <b><u>Specialist Information</u></b>  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                        messageBody += "<tr><td valign=top >  " + currentUser.Name + "  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                        messageBody += "<tr><td valign=top>  " + WorkPhone + "   </td>  <td valign=top >    &nbsp;  </td></tr>";
                                        messageBody += "<tr><td valign=top >  " + currentUser.Email.ToLower() + "  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                        messageBody += "<tr><td valign=top >  &nbsp;  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                        messageBody += "<tr><td valign=top >  &nbsp;  </td>  <td valign=top >    &nbsp;  </td></tr>";
                                        messageBody += "</table></div></body></html>";
                                         */

                                        SPList emailsList = web.Lists["DESREmailRecepients"];
                                        plannerEmail = "";
                                        foreach (SPListItem emailItem in emailsList.Items)
                                        {
                                            if (Convert.ToString(emailItem["Title"]).ToLower() == "planner")
                                            {
                                                plannerEmail = Convert.ToString(emailItem["Email"]);
                                            }
                                        }

                                        currentUserToken = currentUser.UserToken;
                                        currentUserName = currentUser.Name;

                                    } //end of preparing email block
                                }
                            }
                        }
                    }
                }
            });

            if (!string.IsNullOrEmpty(plannerEmail))
            {
                using (SPSite impsite = new SPSite(SPUrl, currentUserToken))
                {
                    using (SPWeb impweb = impsite.OpenWeb())
                    {
                        StringDictionary headers2 = new StringDictionary();
                        headers2.Add("to", plannerEmail);
                        headers2.Add("from", "portaladmin@tams.com");
                        headers2.Add("subject", currentUserName + " Has Added an Additional Comment");

                        SPUtility.SendEmail(impweb, headers2, messageBody);
                    }
                }
            }


            this.AddLog(SPUrl, "ADD-ADDITIONAL-COMMENT", null, authInfo, deviceInfo);
        }
        catch { }
        
        return CreateJsonResponse(actionResultes.ToArray(), callback);
    }

    #endregion

    #region OP:AddLog

    public void AddLog(string SPUrl, string action, string searchText, string authInfo)
    {
        AddLog(SPUrl, action, searchText, authInfo, "");
    }

    public void AddLog(string SPUrl, string action, string searchText, string authInfo, string deviceInfo)
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

                    SqlParameter userDeviceInfo = sqlComm.CreateParameter();
                    userDeviceInfo.ParameterName = "@deviceInfo";
                    userDeviceInfo.DbType = DbType.String;
                    userDeviceInfo.Value = DBNull.Value;
                    if (deviceInfo != null)
                    {
                        userDeviceInfo.Value = deviceInfo;
                    }
                    sqlComm.Parameters.Add(userDeviceInfo);

                    SqlParameter userLongitude = sqlComm.CreateParameter();
                    userLongitude.ParameterName = "@longitude";
                    userLongitude.DbType = DbType.Double;
                    userLongitude.Value = DBNull.Value;
                    Double temLon = 0;
                    if (Double.TryParse(longitude, out temLon))
                    {
                        userLongitude.Value = temLon;
                    }
                    sqlComm.Parameters.Add(userLongitude);

                    SqlParameter userLatitude = sqlComm.CreateParameter();
                    userLatitude.ParameterName = "@latitude";
                    userLatitude.DbType = DbType.Double;
                    userLatitude.Value = DBNull.Value;
                    Double temLat = 0;
                    if (Double.TryParse(latitude, out temLat))
                    {
                        userLatitude.Value = temLat;
                    }

                    sqlComm.Parameters.Add(userLatitude);

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