var pageObj = {
    init: function () {
        // Temperature
        pageObj.curtemp = new JustGage({
            id: "curtemp",
            value: 0.0,
            min: -20,
            max: 50,
            title: "Temperature",
            label: "Celsius",
            gaugeWidthScale: 0.6,
            pointer: true,
            decimals: true,
            levelColors: [
                "#0000ff",
                "#00ff00",
                "#ff0000"
            ]
        });
        // Humidity
        pageObj.curhumid = new JustGage({
            id: "curhumid",
            value: 0,
            min: 0,
            max: 100,
            title: "Humidity",
            label: "%",
            gaugeWidthScale: 0.6,
            pointer: true,
            levelColors: [
                "#0000ff",
                "#00ff00",
                "#ff0000"
            ]
        });
        // Pressure
        pageObj.curpres = new JustGage({
            id: "curpres",
            value: 0,
            min: 900,
            max: 1150,
            title: "Pressure",
            label: "hPa",
            gaugeWidthScale: 0.6,
            pointer: true,
            decimals: true,
            levelColors: [
                "#0000ff",
                "#00ff00",
                "#ff0000"
            ]
        });
        // Indoor Temperature
        pageObj.indoortemp = new JustGage({
            id: "indoortemp",
            value: 0.0,
            min: -20,
            max: 50,
            title: "Temperature",
            label: "Celsius",
            gaugeWidthScale: 0.6,
            pointer: true,
            levelColors: [
                "#0000ff",
                "#00ff00",
                "#ff0000"
            ]
        });
        // Indoor Humidity
        pageObj.indoorhumid = new JustGage({
            id: "indoorhumid",
            value: 0,
            min: 0,
            max: 100,
            title: "Humidity",
            label: "%",
            gaugeWidthScale: 0.6,
            //refreshAnimationType: "bounce",
            pointer: true,
            levelColors: [
                "#0000ff",
                "#00ff00",
                "#ff0000"
            ]
        });

        // init schedule methods
        pageObj.scheduleDivControl();

        // init radio boxes
        pageObj.wsDivControl();

        // enum schedules and populate dropdown
        pageObj.enumScheduleNames();

        // enum built-in functions and populate dropdown
        pageObj.enumBuiltinFuncs();

        // init trusted clients
        pageObj.getTrustedSettings()

        // get home screen values
        pageObj.getWeatherData();
        pageObj.setWeatherInterval();
        pageObj.getData();
        pageObj.setHomeInterval();

        // clear controls
        pageObj.clearPage();

        // enum AppConfig.xml
        pageObj.loadXml();

        // focus command text
        $('input#textinput1').focus();

        // refresh page1
        $('div#page1').page();
    },
    getWeatherData: function () {
        // Weather widget
        $.ajax({
            type: 'GET',
            contentType: 'application/json; charset=utf-8',
            dataType: "json",
            url: "?cmd=%currentcity%&%todayconditions%&%weathericon%&%currenttemperature%&%currenthumidity%&%currentpressure%&mode=json",
        }).done(function (data) {
            if (data.currenttemperature.Value.length <= 0) {
                $('#conditions').html('Unavailable');
                $('#location').hide();
                $('#weather-ico').hide();
            } else {
                $('#conditions').html(data.currenttemperature.Value + '&deg;C');
                $('#location').html(data.currentcity.Value + ', ' + data.todayconditions.Value);
                $('#weather-ico').html('<img src="' + data.weathericon.Value + '"></img>');
                $('#weatherdiv').css('display', 'inline-block').show();
                pageObj.curtemp.refresh(data.currenttemperature.Value);
                pageObj.curhumid.refresh(data.currenthumidity.Value);
                pageObj.curpres.refresh(data.currentpressure.Value);
            }
        }).fail(function () {

        });
    },
    setWeatherInterval: function () {
        setInterval(function () {
            pageObj.getWeatherData();
        }, 60000);
    },
    getData: function () {
        if ($.mobile.activePage.attr("id") == "page0") {
            // Calendar widget
            $.ajax({
                type: 'GET',
                contentType: 'application/json; charset=utf-8',
                dataType: "json",
                url: "?cmd=%day%&%date%&%calendaryear%&%time24%&mode=json",
            }).done(function (data) {
                $('#header').css('display', 'block').show();
                $('#time').html(data.time24.Value);
                $('#date').html(data.day.Value + '<br />' + data.date.Value + ', ' + data.calendaryear.Value);
            }).fail(function () {

            });
            // User status widget
            $.ajax({
                type: 'GET',
                contentType: 'application/json; charset=utf-8',
                dataType: "json",
                url: "?cmd=%salute%&%whoami%&%whereami%&mode=json",
            }).done(function (data) {
                $('#userstat').html('Good ' + data.salute.Value + ' ' + data.whoami.Value + '<br />' + 'Your status is set to ' + data.whereami.Value);
            }).fail(function () {

            });
            // Gmail widget
            $.ajax({
                type: 'GET',
                contentType: 'application/json; charset=utf-8',
                dataType: "json",
                url: "?cmd=%gmailcount%&%gmailreader%&mode=json",
            }).done(function (data) {
                $('#gmail-badge').html(data.gmailcount.Value);
                //$('#gmail').html('Gmail: ' + data.gmailcount.Value);
                $('#gmailreader').html(data.gmailreader.Value.replace(/\r\n|\n|\r/g, '<br />'));
            }).fail(function () {

            });
            // Indoor widget
            // If you have a temperature/humidity sensor attached to your arduino, advice the tutorial below:
            // Tutorial: http://jubitoblog.blogspot.com/2014/06/arduino-temperature-and-humidity-using.html
            // $.get illustrated in example is a shorthand Ajax function. Use either $.get or $.ajax like seeing below at will.
            $.ajax({
                type: 'GET',
                contentType: 'application/json; charset=utf-8',
                dataType: "json",
                url: "?cmd=judo%20serial%20send%20dhttemp&judo%20serial%20send%20humid&mode=json",
            }).done(function (data) {
                if (data.judo_serial_send_dhttemp.Value != 'Serial port state: False') {
                    $('#indoordiv').css('display', 'inline-block').show();
                    $('#indoor').html('Indoor: ' + data.judo_serial_send_dhttemp.Value + '&deg;C' + ' ' + data.judo_serial_send_humid.Value + '%');
                    pageObj.indoortemp.refresh(data.judo_serial_send_dhttemp.Value);
                    pageObj.indoorhumid.refresh(data.judo_serial_send_humid.Value);
                }
            }).fail(function () {

            });
        } else if ($.mobile.activePage.attr("id") == "page1")
            pageObj.refreshReference();
        else if ($.mobile.activePage.attr("id") == "page3") {
            // Flip Toggles
            $.get('?cmd=judo serial state', function (data) {
                if (data.indexOf('Serial port state: True') >= 0) {
                    $('input#sliderSerial').prop('checked', true).flipswitch('refresh'); //.val('on').flipswitch('refresh');
                } else {
                    $('input#sliderSerial').prop('checked', false).flipswitch('refresh'); //.val('off').flipswitch('refresh');
                }
            }, 'html');
            $.get('?cmd=judo socket state', function (data) {
                if (data.indexOf('Socket state: True') >= 0) {
                    $('input#sliderSocket').prop('checked', true).flipswitch('refresh'); //.val('on').flipswitch('refresh');
                } else {
                    $('input#sliderSocket').prop('checked', false).flipswitch('refresh'); //.val('off').flipswitch('refresh');
                }
            }, 'html');
            $.get('?cmd=%about%&%uptime%', function (data) {
                $('div#sysinfo').html(data.replace('Days', 'Uptime: Days'));
            }, 'html');
        }
    },
    setHomeInterval: function () {
        setInterval(function () {
            pageObj.getData();
        }, 5000);
    },
    loadXml: function () {
        $.ajax({
            type: "GET",
            url: "../AppConfig.xml",
            dataType: "xml",
            async: true,
            success: function (xml) {
                $("#customul").empty();
                $("#insetlistAsterisk").empty();
                $("#insetlistReference").empty().append('<option value=" ">&nbsp;</option>');
                $("#insetlistThen").empty().append('<option value=" ">&nbsp;</option>');
                $("#insetlistElse").empty().append('<option value=" ">&nbsp;</option>');
                $("#scheduleAction").empty().append('<option value=" ">&nbsp;</option>');

                $(xml).find('InstructionSet').each(function () {
                    var categ_text = $(this).attr('categ')
                    var header_text = $(this).attr('header')
                    var shortdescr_text = $(this).attr('shortdescr')
                    var descr_text = $(this).attr('descr')
                    var img_src = $(this).attr('img')
                    var id_text = $(this).attr('id')
                    var reference = $(this).attr('ref')

                    if (categ_text != null && categ_text != 'undefined' && $("#ddCategories option[value='" + categ_text + "']").length <= 0)
                        $('#ddCategories').append("<option value='" + categ_text + "'>" + categ_text + "</option>");

                    if (shortdescr_text == null || shortdescr_text.trim() == '')
                        shortdescr_text = '';
                    if (descr_text == null || descr_text.trim() == '')
                        descr_text = '';
                    if (img_src == null || img_src.trim() == '')
                        img_src = '';
                    else
                        img_src = "<img style='position: absolute; top: 50%; left: 5px; margin-top: -40px; float: left; width: 80px; height: 80px; text-align: center; vertical-align: middle;' src='" + img_src + "' />";

                    if (id_text != null && id_text != 'undefined' && id_text.indexOf('*') == 0) {
                        $("#insetlistAsterisk").append("<option value='" + id_text + "'>" + id_text.replace('*', '') + "</option>");
                        $("#insetlistReference").append("<option value='" + id_text + "'>" + id_text.replace('*', '') + "</option>");
                    }
                    if (id_text != null && id_text != 'undefined' && id_text.indexOf('*') == -1) {
                        $("#insetlistThen").append("<option value='" + id_text + "'>" + id_text + "</option>");
                        $("#insetlistElse").append("<option value='" + id_text + "'>" + id_text + "</option>");
                        $("#scheduleAction").append("<option value='" + id_text + "'>" + id_text + "</option>");
                    }

                    if (header_text != null && header_text != 'undefined' && id_text.indexOf('*') == -1 && categ_text == $('#ddCategories').val())
                        if (reference != null) {
                            $("#customul").append(
                                "<li data-theme=\"a\" data-count-theme=\"a\">" +
                                "<a href=\"javascript:pageObj.runCommand('" + id_text + "')\" data-transition=\"slide\">" + img_src +
                                "<h3>" + header_text + "</h3>" +
                                "<p><strong>" + shortdescr_text + "</strong></p>" +
                                "<p>" + descr_text + "</p>" +
                                "<span class='ui-li-count' id='" + reference + "' style='font-size: 10px;'></span></a>" +
                                "</li>"
                            );
                        }
                        else {
                            $("#customul").append(
                                "<li data-theme=\"a\" data-count-theme=\"a\">" +
                                "<a href=\"javascript:pageObj.runCommand('" + id_text + "')\" data-transition=\"slide\">" + img_src +
                                "<h3>" + header_text + "</h3>" +
                                "<p><strong>" + shortdescr_text + "</strong></p>" +
                                "<p>" + descr_text + "</p></a>" +
                                "</li>"
                            );
                        }
                }); //close each(
            },
            complete: function () {
                $('#ddCategories').selectmenu().selectmenu('refresh');
                $('#customul').listview().listview('refresh');
            }
        }) //close $.ajax(
    },
    clearPage: function () {
        $('input#textinput1').val('');
        $('div#response-p1').html('');
        $('div#response-p2').html('');
        $('input#textinput1').focus();
    },
    checkKey: function (event) {
        if (event.keyCode == 13) {
            pageObj.runCommand(encodeURIComponent($('input#textinput1').val()))
        }
    },
    refreshReference: function () {
        $("#customul span").each(function (i) {
            pageObj.runAsyncCommand('{mute}' + this.id);
        });
    },
    runAsyncCommand: function (cmd) {
        var element = cmd.replace('{mute}', '');
        $.ajax({
            type: 'GET',
            url: '?cmd=' + cmd,
            dataType: 'html',
            async: true,
            success: function (data) {
                $("#" + element).html(data);
            }
        });
    },
    runCommand: function (cmd) {
        if (cmd != "") {
            $.get('?cmd=' + cmd, function (data) {
                if (data != '') {
                    if ($.mobile.activePage.attr("id") == "page2")
                        $('div#response-p2').html(data);
                    else {
                        $('div#response-p1').html(data + $('div#closeButton').html());
                        $('div#popup-response-p1').popup('open');
                    }
                } else {
                    if ($.mobile.activePage.attr("id") == "page0")
                        // Do nothing
                        ;
                    else if ($.mobile.activePage.attr("id") == "page2")
                        $('div#response-p2').html('Operation completed.');
                    else {
                        $('div#response-p1').html('Operation completed.' + $('div#closeButton').html());
                        $('div#popup-response-p1').popup('open');
                    }
                }
            }, 'html');
        }
    },
    response3Popup: function (data) {
        $('div#response-p3').html(data + $('div#closeButton').html());
        $('div#popup-response-p3').popup('open');
    },
    sendSMS: function () {
        if ($('input#phonenumber').val() != '' && $('input#smsText').val() != '') {
            $.get('?cmd=judo sms send ' + $('input#phonenumber').val() + ' `' + $('input#smsText').val() + '`', function (data) {
                pageObj.response3Popup(data);
            }, 'html');
        }
        $('input#phonenumber').val('');
        $('input#smsText').val('');
        window.history.back();
    },
    viewSettings: function (acc) {
        $.get('?cmd=judo ' + acc + ' settings', function (data) {
            pageObj.response3Popup(data);
        }, 'html');
    },
    gmailSettings: function () {
        if ($('input#gmailUsername').val() != '' && $('input#gmailPassword').val() != '') {
            $.get('?cmd=judo gmail set ' + $('input#gmailUsername').val() + ' ' + encodeURIComponent($('input#gmailPassword').val()), function (data) {
                pageObj.response3Popup(data);
            }, 'html');
        }
        $('input#gmailUsername').val('');
        $('input#gmailPassword').val('');
        window.history.back();
    },
    smtpSettings: function () {
        if ($('input#smtpHost').val() != '' && $('input#smtpUsername').val() != '' && $('input#smtpPassword').val() != '' && $('input#smtpPort').val() != '') {
            $.get('?cmd=judo smtp set ' + $('input#smtpHost').val() + ' ' + $('input#smtpUsername').val() + ' ' + encodeURIComponent($('input#smtpPassword').val()) + ' ' + $('input#smtpPort').val() + ' ' + $('input#chkSmtpSSL').prop('checked'), function (data) {
                pageObj.response3Popup(data);
            }, 'html');
        }
        $('input#smtpHost').val('');
        $('input#smtpUsername').val('');
        $('input#smtpPassword').val('');
        $('input#smtpPort').val('');
        window.history.back();
    },
    pop3Settings: function () {
        if ($('input#pop3Host').val() != '' && $('input#pop3Username').val() != '' && $('input#pop3Password').val() != '' && $('input#pop3Port').val() != '') {
            $.get('?cmd=judo pop3 set ' + $('input#pop3Host').val() + ' ' + $('input#pop3Username').val() + ' ' + encodeURIComponent($('input#pop3Password').val()) + ' ' + $('input#pop3Port').val() + ' ' + $('input#chkPop3SSL').prop('checked'), function (data) {
                pageObj.response3Popup(data);
            }, 'html');
        }
        $('input#pop3Host').val('');
        $('input#pop3Username').val('');
        $('input#pop3Password').val('');
        $('input#pop3Port').val('');
        window.history.back();
    },
    mailheaderSettings: function () {
        if ($('input#mailFrom').val() != '' && $('input#mailTo').val() != '' && $('input#mailSubject').val() != '') {
            $.get('?cmd=judo mailheaders set `' + $('input#mailFrom').val() + '` `' + $('input#mailTo').val() + '` `' + $('input#mailSubject').val() + '`', function (data) {
                pageObj.response3Popup(data);
            }, 'html');
        }
        $('input#mailFrom').val('');
        $('input#mailTo').val('');
        $('input#mailSubject').val('');
        window.history.back();
    },
    smsSettings: function () {
        if ($('input#smsAPI').val() != '' && $('input#smsUsername').val() != '' && $('input#smsPassword').val() != '') {
            $.get('?cmd=judo sms set ' + $('input#smsAPI').val() + ' ' + $('input#smsUsername').val() + ' ' + encodeURIComponent($('input#smsPassword').val()), function (data) {
                pageObj.response3Popup(data);
            }, 'html');
        }
        $('input#smsAPI').val('');
        $('input#smsUsername').val('');
        $('input#smsPassword').val('');
        window.history.back();
    },
    serverLogin: function () {
        if ($('input#serverUsername').val() != '' && $('input#serverPassword').val() != '') {
            $.get('?cmd=judo server login ' + $('input#serverUsername').val() + ' ' + encodeURIComponent($('input#serverPassword').val()), function (data) {
                pageObj.response3Popup(data);
            }, 'html');
        }
        $('input#serverUsername').val('');
        $('input#serverPassword').val('');
        window.history.back();
    },
    serverSettings: function () {
        if ($('input#serverHost').val() != '' && $('input#serverPort').val() != '') {
            $.get('?cmd=judo server set ' + $('input#serverHost').val() + ' ' + $('input#serverPort').val() + ' ' + $('select#serverAuth').val(), function (data) {
                pageObj.response3Popup(data);
            }, 'html');
        }
        $('input#serverHost').val('');
        $('input#serverPort').val('');
        window.history.back();
    },
    socketSettings: function () {
        if ($('input#socketHost').val() != '' && $('input#socketPort').val() != '') {
            $.get('?cmd=judo socket set ' + $('input#socketHost').val() + ' ' + $('input#socketPort').val(), function (data) {
                pageObj.response3Popup(data);
            }, 'html');
        }
        $('input#socketHost').val('');
        $('input#socketPort').val('');
        window.history.back();
    },
    getTrustedSettings: function () {
        $.get('?cmd=judo trusted settings', function (data) {
            $('input#trusted').val(data);
        }, 'html');
    },
    trustedSettings: function () {
        if ($('input#trusted').val() != '') {
            $.get('?cmd=judo socket trust <lock>' + $('input#trusted').val() + '</lock>', function (data) {
                pageObj.response3Popup(data);
            }, 'html');
        }
        //$('input#trusted').val('');
        pageObj.getTrustedSettings();
        window.history.back();
    },
    serialSettings: function () {
        if ($('input#serialPort').val() != '' && $('input#serialBaud').val() != '') {
            $.get('?cmd=judo serial set ' + $('input#serialPort').val() + ' ' + $('input#serialBaud').val(), function (data) {
                pageObj.response3Popup(data);
            }, 'html');
        }
        $('input#serialPort').val('');
        $('input#serialBaud').val('');
        window.history.back();
    },
    weatherSettings: function () {
        if ($('input#weatherURI').val() != '') {
            $.get('?cmd=judo weather set <lock>' + $('input#weatherURI').val() + '</lock>', function (data) {
                pageObj.response3Popup(data);
            }, 'html');
        }
        $('input#weatherURI').val('');
        window.history.back();
    },
    saveSchedule: function () {
        var scheduleID = $('input#scheduleName').val().replace(/ /g, '_');
        var date;
        var time;
        var action;

        if ($('select#schedulePeriod option:selected').text() == 'Specific Date') {
            date = $('input#scheduleDate').val();
            time = $('input#scheduleTime').val();
        } else if ($('select#schedulePeriod option:selected').text() == 'Repeat') {
            date = 'repeat';
            time = $('input#scheduleRepeat').val();
        } else {
            date = $('select#schedulePeriod option:selected').val();
            time = $('input#scheduleTime').val();
        }

        if ($('input#scheduleOther').val() != '')
            action = $('input#scheduleOther').val();
        else
            action = $('select#scheduleAction').val();

        $.get('?cmd=judo schedule add ' + scheduleID + ' ' + date + ' ' + time + ' `' + action + '`', function (data) {
            pageObj.response3Popup(data);
        }, 'html');

        $('input#scheduleName').val('');
        $('input#scheduleDate').val('');
        $('select#schedulePeriod').prop('selectedIndex', 0);
        $('select#schedulePeriod').selectmenu('refresh');
        $('input#scheduleTime').val('');
        $('input#scheduleRepeat').val('');
        $('select#scheduleAction').prop('selectedIndex', 0);
        $('select#scheduleAction').selectmenu('refresh');
        $('input#scheduleOther').val('');
        pageObj.enumScheduleNames();
        pageObj.scheduleDivControl();
        window.history.back();
    },
    enumScheduleNames: function () {
        $('select#ddScheduleNames').empty();
        $('select#ddScheduleNames').append("<option value=''></option>");
        $.get('?cmd=judo schedule name-list', function (data) {
            var items = data.split('<br /\>');
            for (i = 0; i < items.length; i++)
                $('select#ddScheduleNames').append("<option value='" + items[i] + "'>" + items[i] + "</option>");
        }, 'html');
    },
    changeScheduleStatus: function (stat) {
        if ($('select#ddScheduleNames option:selected').text() != '') {
            $.get('?cmd=judo schedule ' + stat + ' ' + $('select#ddScheduleNames option:selected').text(), function (data) {
                pageObj.response3Popup(data);
            }, 'html');
            pageObj.enumScheduleNames();
            $('select#ddScheduleNames').prop('selectedIndex', 0);
            $('select#ddScheduleNames').selectmenu('refresh');
            alert('Operation completed.');
        }
    },
    scheduleAction: function (action) {
        $.get('?cmd=judo schedule ' + action, function (data) {
            pageObj.response3Popup(data);
        }, 'html');
        pageObj.enumScheduleNames();
        $('select#ddScheduleNames').prop('selectedIndex', 0);
        $('select#ddScheduleNames').selectmenu('refresh');
        if (action == 'enable-all' || action == 'disable-all' || action == 'remove-all') {
            alert('Operation completed.');
        }
    },
    enumBuiltinFuncs: function () {
        var builtin_functions = {
            "%mute%": "Mute",
            "%unmute%": "Unmute",
            "%inetcon%": "Check internet connection",
            "%gmailcount%": "Count of unread gmail messages",
            "%gmailreader%": "Get unread gmail sender info & subject",
            "%gmailheaders%": "Gmail header info",
            "%pop3count%": "Count of POP3 account",
            "%whoami%": "Get user login",
            "%checkin%": "Check-in user",
            "%checkout%": "Check-out user",
            "%time%": "Get system time",
            "%time24%": "Get system time 24h",
            "%hour%": "Get system hour",
            "%minute%": "Get system minute",
            "%date%": "Get system date (e.g. November 5)",
            "%calendardate%": "Get system date (d/m/yyyy)",
            "%day%": "Get day (e.g. Friday)",
            "%calendarday%": "Get calendar day (e.g. 17)",
            "%calendarmonth%": "Get calendar month (e.g. 11)",
            "%calendaryear%": "Get calendar year (e.g. <script>document.write(new Date().getFullYear())</script>)",
            "%salute%": "Salute in human means (e.g. good morning, good evening, etc)",
            "%partofday%": "Part of day (e.g. morning, afternoon, etc)",
            "%todayconditions%": "Current weather conditions (Weather API)",
            "%todaylow%": "Current low temperature (Weather API)",
            "%todayhigh%": "Current high temperature (Weather API)",
            "%currenttemperature%": "Current temperature (Weather API)",
            "%currenthumidity%": "Current humidity (Weather API)",
            "%currentpressure%": "Current pressure (Weather API)",
            "%currentcity%": "Current city (Weather API)",
            "%whereami%": "Get user status",
            "%uptime%": "System uptime",
            "%updays%": "System uptime, days",
            "%uphours%": "System uptime, hours",
            "%upminutes%": "System uptime, minutes",
            "%upseconds%": "System uptime, seconds",
            "%about%": "About"
        }

        $('select#insetlistFunc').empty();
        $.each(builtin_functions, function (key, value) {
            $('select#insetlistFunc').append($('<option />').attr("value", key).text(value));
        });
    },
    saveLauncher: function () {
        var launcherID = $('input#launcherName').val().replace(/ /g, '_');

        if (launcherID != '' && $('input#launcherAction').val() != '') {
            $.get('?cmd=judo inset add ' + launcherID + ' <lock>' + $('input#launcherAction').val() + '</lock>', function (data) {
                pageObj.response3Popup(data);
                $('input#launcherName').val('');
                $('input#launcherAction').val('');
            }, 'html');
        }
        setTimeout(function () {
            pageObj.loadXml();
        }, 2000);
        window.history.back();
    },
    saveEvent: function () {
        var eventID = $('input#eventName').val().replace(/ /g, '_');

        if (eventID != '' && $('input#eventAction').val() != '') {
            $.get('?cmd=judo event add ' + eventID + ' <lock>' + $('input#eventAction').val() + '</lock>', function (data) {
                pageObj.response3Popup(data);
                $('input#eventName').val('');
                $('input#eventAction').val('');
            }, 'html');
        }
        setTimeout(function () {
            pageObj.loadXml();
        }, 2000);
        window.history.back();
    },
    saveEvaluator: function () {
        var evalID = $('input#insetName4Eval').val().replace(/ /g, '_');

        if (evalID != '' && $('input#insetAction4Eval').val() != '') {
            $.get('?cmd=judo inset add ' + evalID + ' <lock>' + $('input#insetAction4Eval').val() + '</lock>', function (data) {
                if (data.indexOf('Element added.') >= 0)
                    pageObj.clearEvaluatorFields();
                alert(data);
            }, 'html');
        }
        setTimeout(function () {
            pageObj.loadXml();
        }, 2000);
    },
    clearEvaluatorFields: function () {
        $('input#insetName4Eval').val('');
        $('input#insetAction4Eval').val('');
        $('input#evalBoolIF').val('');
        $('input#evalBoolTHIS').val('');
        $('select#evalBoolCondition').prop('selectedIndex', 0);
        $('select#evalBoolCondition').selectmenu('refresh');
        $('select#insetlistThen').prop('selectedIndex', 0);
        $('select#insetlistThen').selectmenu('refresh');
        $('select#insetlistElse').prop('selectedIndex', 0);
        $('select#insetlistElse').selectmenu('refresh');
        return;
    },
    saveWebService: function () {
        var wsID = $('input#wsName').val().replace(/ /g, '_');

        var judo;
        if ($('#radio-json').is(':checked'))
            judo = 'judo json add';
        else {
            $('#wsNamespace').show();
            $('#wsIndex').show();
            judo = 'judo xml add';
        }
        if (wsID != '' && $('input#wsEndpoint').val() != '' && $('input#wsNode').val() != '' && $('input#wsNamespace').val() != '') {
            $.get('?cmd=' + judo + ' ' + wsID + ' <lock>' + encodeURIComponent($('input#wsEndpoint').val()) + '</lock> `' + $('input#wsNamespace').val() + '` `' + $('input#wsNode').val() + '` `' + $('input#wsIndex').val() + '`', function (data) {
                if (data.indexOf('Element added.') >= 0)
                    pageObj.clearWSFields();
                pageObj.response3Popup(data);
            }, 'html');
        } else if (wsID != '' && $('input#wsEndpoint').val() != '' && $('input#wsNode').val() != '') {
            $.get('?cmd=' + judo + ' ' + wsID + ' <lock>' + encodeURIComponent($('input#wsEndpoint').val()) + '</lock> `' + $('input#wsNode').val() + '` `' + $('input#wsIndex').val() + '`', function (data) {
                if (data.indexOf('Element added.') >= 0)
                    pageObj.clearWSFields();
                pageObj.response3Popup(data);
            }, 'html');
        }
        setTimeout(function () {
            pageObj.loadXml();
        }, 2000);
        window.history.back();
    },
    clearWSFields: function () {
        $('input#wsName').val('');
        $('input#wsEndpoint').val('');
        $('input#wsNamespace').val('');
        $('input#wsNode').val('');
        $('input#wsIndex').val('');
        return;
    },
    saveInset: function () {
        var insetID = $('input#insetName').val().replace(/ /g, '_');

        if (insetID != '' && $('input#insetAction').val() != '' && $('input#insetCateg').val() != '' && $('input#insetHeader').val() != '') {
            $.get('?cmd=judo inset add ' + insetID + ' <lock>' + $('input#insetAction').val() + '</lock> `' + $('input#insetCateg').val() + '` `' + $('input#insetHeader').val() + '` `' + $('input#insetShortDescr').val() + '` `' + $('input#insetDescr').val() + '` `' + $('input#insetThumbnail').val() + '` `' + $('select#insetlistReference').val().replace('*', '') + '`', function (data) {
                if (data.indexOf('Element added.') >= 0)
                    pageObj.clearInsetFields();
                alert(data);
            }, 'html');
        } else if (insetID != '' && $('input#insetAction').val() != '') {
            $.get('?cmd=judo inset add ' + insetID + ' <lock>' + $('input#insetAction').val() + '</lock>', function (data) {
                if (data.indexOf('Element added.') >= 0)
                    pageObj.clearInsetFields();
                alert(data);
            }, 'html');
        }
        setTimeout(function () {
            pageObj.loadXml();
        }, 2000);
    },
    clearInsetFields: function () {
        $('input#insetName').val('');
        $('input#insetAction').val('');
        $('input#insetCateg').val('');
        $('input#insetHeader').val('');
        $('input#insetShortDescr').val('');
        $('input#insetDescr').val('');
        $('input#insetThumbnail').val('');
        $('input#evalBoolIF').val('');
        $('input#evalBoolTHIS').val('');
        $('select#insetlistAsterisk').prop('selectedIndex', 0);
        $('select#insetlistAsterisk').selectmenu('refresh');
        $('select#insetlistReference').prop('selectedIndex', 0);
        $('select#insetlistReference').selectmenu('refresh');
        $('select#insetlistFunc').prop('selectedIndex', 0);
        $('select#insetlistFunc').selectmenu('refresh');
        return;
    },
    removeElement: function () {
        var removalID = $('input#elementNameRemove').val().replace(/ /g, '_');

        if (removalID != '') {
            var p, m;

            if ($('input#chkEvent').prop('checked')) {
                p = '?cmd=judo inset remove ' + removalID + '& judo event remove ' + removalID;
                m = 'Elements removed.';
            } else {
                p = '?cmd=judo inset remove ' + removalID;
                m = 'Element removed.';
            }

            $.get(p, function (data) {
                $('div#response-p3').html(m + $('div#closeButton').html());
                $('div#popup-response-p3').popup('open');
                $('input#elementNameRemove').val('');
            }, 'html');
        }
        setTimeout(function () {
            $("#ddCategories").empty();
            pageObj.loadXml();
        }, 2000);
        window.history.back();
    },
    addInsetFunc: function () {
        if ($("select#insetlistFunc").val() == null)
            return;
        $('input#insetAction').val($('input#insetAction').val() + ' ' + $("select#insetlistFunc").val());
    },
    addInsetAsterisk: function () {
        if ($("select#insetlistAsterisk").val() == null)
            return;
        $('input#insetAction').val($('input#insetAction').val() + ' ' + $("select#insetlistAsterisk").val());
    },
    addEval: function () {
        var param1 = $('input#evalBoolIF').val().trim();
        var param2 = $('input#evalBoolTHIS').val().trim();
        var cond = $('select#evalBoolCondition').val();

        if (cond == '==' || cond == '!=') {
            if (!($.isNumeric(param1)) && !($.isNumeric(param2))) {
                param1 = '"' + param1 + '"';
                param2 = '"' + param2 + '"';
            }
        }
        $('input#insetAction4Eval').val($('input#insetAction4Eval').val() + ' { evalBool(' + param1 + cond + param2 + '); ' + $("select#insetlistThen").val() + '; ' + $("select#insetlistElse").val() + '; } ');
    },
    addCalendarDate: function (data) {
        if (data == 'clear')
            $('input#scheduleDate').val('');
        else
            $('input#scheduleDate').val('%calendardate%');
    },
    serialStatus: function () {
        if ($('input#sliderSerial').is(':checked')) //.val() == 'on')
            $.get('?cmd=judo serial open', function (data) {
                //pageObj.response3Popup(data);
            }, 'html');
        else if ($('input#sliderSerial').is(!':checked')) //.val() == 'off')
            $.get('?cmd=judo serial close', function (data) {
                //pageObj.response3Popup(data);
            }, 'html');
    },
    socketStatus: function () {
        if ($('input#sliderSocket').is(':checked')) //.val() == 'on')
            $.get('?cmd=judo socket open', function (data) {
                //pageObj.response3Popup(data);
            }, 'html');
        else if ($('input#sliderSocket').is(!':checked')) //.val() == 'off')
            $.get('?cmd=judo socket close', function (data) {
                //pageObj.response3Popup(data);
            }, 'html');
    },
    wsDivControl: function () {
        $('div#wsHiddenInputs').hide();

        $('#radio-json').on('click', function () {
            $('div#wsHiddenInputs').hide();
        });

        $('#radio-xml').on('click', function () {
            $('div#wsHiddenInputs').show();
        });
    },
    scheduleDivControl: function () {
        pageObj.initScheduleDivs();

        $('select#schedulePeriod').on('change', function () {
            if ($('select#schedulePeriod').val() == 'repeat') {
                $('div#specificDate').hide();
                $('div#time24').hide();
                $('div#period').show();
            } else if ($('select#schedulePeriod').val() == 'date') {
                $('div#period').hide();
                $('div#specificDate').show();
                $('div#time24').show();
            } else {
                pageObj.initScheduleDivs();
            }
        });
    },
    initScheduleDivs: function () {
        $('div#period').hide();
        $('div#specificDate').hide();
        $('div#time24').show();
    }
}