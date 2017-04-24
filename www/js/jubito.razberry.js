var root = "http://localhost:8083/ZWaveAPI/";
var AddNodeToNetwork_State = 0;
var RemoveNodeFromNetwork_State = 0;

function getData() {
	$.getJSON(root + "Data", function (data) {
		var output = '';

		$.each(data.devices, function (index, value) {
			//alert(index + ' ' + value + ' ' + data.devices[index].data.givenName.value);
			// Ping devices
			root + 'Run/devices[' + index + '].SendNoOperation()';
			var devName = data.devices[index].data.vendorString.value;

			if (data.devices[index].data.givenName.value != '')
				devName = data.devices[index].data.givenName.value;

			if (devName != '') {
				var descr = 'Level: ' + data.devices[index].instances[0].commandClasses[50].data[0].val.value + ' | Scale: ' + data.devices[index].instances[0].commandClasses[50].data[0].scaleString.value;
				var level = data.devices[index].instances[0].commandClasses[37].data.level.value.toString().replace('true', 'On').replace('false', 'Off');
				var cmdOff = root + 'Run/devices[' + index + '].instances[0].commandClasses[37].Set(0)';
				var cmdOn = root + 'Run/devices[' + index + '].instances[0].commandClasses[37].Set(255)';
				var updateTime = data.devices[index].instances[0].commandClasses[37].data.level.updateTime;
				var date = new Date(updateTime * 1000);
				// hours part from the timestamp
				var hours = date.getHours();
				// minutes part from the timestamp
				var minutes = "0" + date.getMinutes();
				// seconds part from the timestamp
				var seconds = "0" + date.getSeconds();
				var formattedTime = hours + ':' + minutes.substr(minutes.length - 2) + ':' + seconds.substr(seconds.length - 2);
				var cmd;

				if (level == 'On')
					cmd = cmdOff;
				else
					cmd = cmdOn;

				var list_item = '<li><a href=javascript:runCommand("' + cmd + '")>' + '#' + index + ' ' + devName + '<span class="ui-li-count">' + level.replace('On', '<font color=green>On</font>').replace('Off', '<font color=red>Off</font>') + '</span>' +
					'<p><strong>Last Update: ' + formattedTime + '</strong></p>' +
					'<p>' + descr + '</p>' +
					'</a></li>';

				output += list_item;
			}
		});
		$('#devices').html(output).listview("refresh");
	});
}

function runCommand(cmd) {
	$.get(cmd, function (data) {
		return (data);
	});
}

function NetworkNodes(state) {
	switch (state) {
		case 'add':
			// Start
			if (AddNodeToNetwork_State == 0) {
				AddNodeToNetwork_State = 1;
				$.get(root + 'Run/controller.AddNodeToNetwork(1)');
			}
			// Stop
			else {
				AddNodeToNetwork_State = 0;
				$.get(root + 'Run/controller.AddNodeToNetwork(0)');
			}
			break;
		case 'remove':
			// Start
			if (RemoveNodeFromNetwork_State == 0) {
				RemoveNodeFromNetwork_State = 1;
				$.get(root + 'Run/controller.RemoveNodeFromNetwork(1)');
			}
			// Stop
			else {
				RemoveNodeFromNetwork_State = 0;
				$.get(root + 'Run/controller.RemoveNodeFromNetwork(0)');
			}
			break;
	}
}