let _logsUrl = "";
let _getLogsUrl = "";
let logData = [];
let _connection = null;
let logsDiv;
let _maxElements = 100;
let _loadMoreButton;
let typingTimer;
let doneTypingInterval = 1000;

const FINAL_ACTION_KEYWORDS = [
	"attack sent",
	"fleet sent",
	"recycler",
	"gesendet",
	"cancelled",
	"canceled",
	"abgebrochen",
	"done",
	"erledigt",
	"abgeschlossen",
	"recalled",
	"zurueckgerufen",
];

function isFinalAction(message) {
	if (!message) return false;
	let lower = message.toLowerCase();
	return FINAL_ACTION_KEYWORDS.some(keyword => lower.includes(keyword));
}

function Initialize(logsUrl, getLogsUrl) {
	showLoading();
	_logsUrl = logsUrl;
	_getLogsUrl = getLogsUrl;
	_loadMoreButton = $("#loadMore").parent();
	logsDiv = $("#logsDiv");
	initHideDataBox();

	$('#logsearch').on('input propertychange paste', function () {
		clearTimeout(typingTimer);
		typingTimer = setTimeout(applyFilter, doneTypingInterval);
	});

	$("#logsDate").on("change blur", function () {
		let inputValue = $(this).val();
		if (inputValue == null || inputValue === "")
			return;
		let newUrl = window.location.href.split("?")[0];
		let newDate = formatDate(new Date(inputValue));
		window.location = `${newUrl}?date=${newDate}`;
	});

	let logsDate = new Date($("#logsDate").val());
	updateGrid(_getLogsUrl + getFilterParameters(), function () {
		if (logsDate.toDateString() === new Date().toDateString()) {
			defineConnection();
			start();
		}
	});
}

function ensureLogDataOrder() {
	let previousDate = logData[0].date;
	for (let i = 1; i < logData.length; i++) {
		if (logData[i].date > previousDate) {
			alert(`Position: ${i}. LogDate: ${logData[i].date}. Previous Date: ${previousDate}`);
			return;
		}
		previousDate = logData[i].date;
	}
	alert("Everything is ok");
}

function updateGrid(url, callback) {
	showLoading();
	$.get(url,
		function (data) {
			logData = data.content.map(x => new LogEntry(x.datetime, x.type, x.message, x.sender, x.position));
			_maxElements = logData.length > 100 ? logData.length : 100;
			renderGrid(logData);
			if (callback)
				callback();
			manageVisibilityLoadMore(data.hasMoreData);
			if (data.content.length == 0) {
				logsDiv.prepend("<div class='row'><span>No logs to show.</span></div>")
			}
			hideLoading();
		}
	);

}

function padTo2Digits(num) {
	return num.toString().padStart(2, '0');
}

function formatDate(date) {
	return [
		padTo2Digits(date.getDate()),
		padTo2Digits(date.getMonth() + 1),
		date.getFullYear(),
	].join('/');
}

function LogEntry(timestamp, level, message, sender, position) {
	this.date = new Date(timestamp);
	this.timestamp = this.date.toLocaleString();
	this.originalTimestamp = timestamp;
	this.level = level;
	this.loglevel = getLogLevelNum(level);
	this.message = message;
	this.sender = sender;
	this.position = position;
	this.row = createLogElement(this.timestamp, this.level, this.message, this.sender);
}

function getFilterParameters() {
	let logLevel = $("#loglevelfilter option:selected").val();
	let textToSearch = $("#logsearch").val().toLowerCase();
	let sender = $("#logsenderfilter option:selected").val();
	let filterParameters = `&search=${encodeURIComponent(textToSearch)}&logSender=${encodeURIComponent(sender)}&logLevel=${encodeURIComponent(logLevel)}`;
	return filterParameters;
}

function applyFilter() {
	let urlWithFilters = _getLogsUrl + getFilterParameters();
	updateGrid(urlWithFilters);
}

function cleanGrid() {
	logsDiv.find(".row").remove();
}

function renderRow(row) {
	logsDiv.append(row);
}

function renderRowTop(row) {
	logsDiv.prepend(row);
}

function renderGrid(data) {
	cleanGrid();
	for (let i = 0; i < data.length; i++) {
		renderRow(data[i].row);
	}
}


function manageVisibilityLoadMore(show) {
	if (show) {
		_loadMoreButton.show();
	}
	else {

		_loadMoreButton.hide();
	}
}

function loadMore() {
	if (logData.length == 0) {
		hideLoading();
		return;
	}

	let last = logData[logData.length - 1];
	let timestamp = last.originalTimestamp;
	let url = _getLogsUrl + "&lastTime=" + encodeURIComponent(timestamp) + getFilterParameters();
	showLoading();
	$.get(url,
		function (data) {
			let logEntries = data.content.map(x => new LogEntry(x.datetime, x.type, x.message, x.sender, x.position));
			_maxElements += logEntries.length;
			for (let i = 0; i < logEntries.length; i++) {
				logData.push(logEntries[i]);
				renderRow(logEntries[i].row);
			}
			manageVisibilityLoadMore(data.hasMoreData);
			hideLoading();
		});
}

function getLogLevelNum(logLevel) {
	switch (logLevel) {
		case "Trace":
			return 0;
		case "Debug":
			return 1;
		case "Information":
			return 2;
		case "Warning":
			return 3;
		case "Error":
			return 4;
	}
}

const MASK_STOPWORDS = new Set(["to", "from", "at", "on", "in", "for", "of", "the", "and", "or", "with", "by", "via", "is", "no", "nach", "von", "bei", "auf", "in", "fuer", "und", "oder", "mit", "zu", "zum", "zur", "der", "die", "das", "an", "am", "im", "target", "origin", "celestial", "planet", "moon", "colony", "->", "=>", "-", ":"]);
function isHideData() {
	return window.tbotHideData ? window.tbotHideData.get() : false;
}
function maskText(text) {
	if (!text) return text;
	let t = String(text).replace(/\[[^\]\s]+@[^\]\s]+\]/g, "[*****]");
	t = t.replace(/((?:[^\s\[\]]+ ){1,3})(\[[PMpm]?:?\d+:\d+:\d+(?::[PMpm])?\])/g, function (m, words, coord) {
		let parts = words.trim().split(" ");
		let keep = [];
		while (parts.length) {
			let w = parts[parts.length - 1];
			if (MASK_STOPWORDS.has(w.toLowerCase()) || /[:,.;)(]$/.test(w)) break;
			parts.pop();
			keep.push(w);
		}
		if (!keep.length) return m;
		return (parts.length ? parts.join(" ") + " " : "") + "***** " + coord;
	});
	t = t.replace(/\[[PMpm]?:?\d+:\d+:\d+(?::[PMpm])?\]|\b\d:\d{1,3}:\d{1,2}(?::[PMpm])?\b/g, "[*****]");
	return t;
}
function applyMaskToRows() {
	for (const e of logData) {
		e.row.find(".log-msg").text(isHideData() ? maskText(e.message) : e.message);
	}
}
function initHideDataBox() {
	let box = document.getElementById("loghide");
	if (!box) return;
	box.checked = isHideData();
	box.addEventListener("change", function () {
		if (window.tbotHideData) window.tbotHideData.set(box.checked);
		applyMaskToRows();
	});
}

function createLogElement(timestamp, level, message, sender) {
	let row = $("<div class='row logEntry'></div>");
	let colTs = $("<div class='col-5 col-lg-3 col-xl-2 log-ts'></div>").text(timestamp)
		.addClass("timestamp");

	let colSender = $("<div class='col-4 col-lg-2 col-xl-2 log-sender'></div>").text(sender)
		.addClass("logsender");

	let colLevel = $("<div class='col-3 col-lg-2 col-xl-1 log-level'></div>").text(level);
	colLevel.addClass(level)
		.addClass("loglevel");

	let colMsg = $("<div class='col-12 log-msg'></div>").text(isHideData() ? maskText(message) : message)
		.addClass("logmessage");

	row.append(colTs)
		.append(colSender)
		.append(colLevel)
		.append(colMsg);

	if (isFinalAction(message)) {
		colMsg.addClass("final-action");
		colMsg.css({ "color": "#2ecc71", "font-weight": "600" });
	}

	return row;
}

function renderLogEntry(timestamp, level, message, sender) {
	let lastPosition = 0;
	if (logData.length > 0)
		lastPosition = logData[0].position;
	let logEntry = new LogEntry(timestamp, level, message, sender, lastPosition + 1);

	logData.unshift(logEntry);

	let logLevelSelected = getLogLevelNum($("#loglevelfilter option:selected").val());
	let render = true;

	if (logLevelSelected !== 0 && logEntry.loglevel < logLevelSelected)
		render = false;

	let searchText = $("#logsearch").val();
	if (searchText !== "" && !message.toLowerCase().includes(searchText.toLowerCase()))
		render = false;

	let logSenderSelected = $("#logsenderfilter option:selected").val();
	if (logSenderSelected !== "" && sender !== logSenderSelected)
		render = false;

	if (render)
		renderRowTop(logEntry.row);

	let elementsCount = logsDiv.find(".row").length;
	if (elementsCount > _maxElements) {
		let elementsToDelete = elementsCount - _maxElements;
		logsDiv.find(".row:nth-last-child(-n+" + elementsToDelete + ")").remove();
		logData = logData.slice(0, -elementsToDelete);
	}
}

let _hadConnection = false;
let _catchingUp = false;
let _starting = false;

function defineConnection() {
	_connection = new signalR.HubConnectionBuilder()
		.withUrl(_logsUrl)
		.configureLogging(signalR.LogLevel.Warning)
		.withAutomaticReconnect({
			nextRetryDelayInMilliseconds: (ctx) => [0, 2000, 5000, 10000][ctx.previousRetryCount] ?? 15000
		})
		.build();

	_connection.serverTimeoutInMilliseconds = 10 * 60 * 1000;
	_connection.keepAliveIntervalInMilliseconds = 15 * 1000;

	_connection.onclose(async () => {
		await start();
	});

	_connection.onreconnected(() => {
		catchUp();
	});

	_connection.on("SendLogAsObject", (data) => {
		renderLogEntry(data.timestamp, data.level, data.message, data.LogSender);
	});

	document.addEventListener("visibilitychange", () => {
		if (document.visibilityState !== "visible" || !_connection)
			return;
		if (_connection.state === signalR.HubConnectionState.Disconnected)
			start();
		else if (_connection.state === signalR.HubConnectionState.Connected)
			catchUp();
	});
}

async function start() {
	if (_starting || _connection.state !== signalR.HubConnectionState.Disconnected)
		return;
	_starting = true;
	try {
		await _connection.start();
		console.log("SignalR Connected.");
		_starting = false;
		if (_hadConnection)
			catchUp();
		_hadConnection = true;
	} catch (err) {
		_starting = false;
		console.log(err);
		setTimeout(start, 5000);
	}
};

function logKey(date, sender, message) {
	return Math.floor(date.getTime() / 1000) + "|" + sender + "|" + message;
}

async function catchUp() {
	if (_catchingUp || !logData.length)
		return;
	_catchingUp = true;
	try {
		let newest = logData[0].date.getTime();
		let known = new Set(logData.slice(0, 500).map(e => logKey(e.date, e.sender, e.message)));
		let missing = [];
		let url = _getLogsUrl;
		for (let page = 0; page < 20; page++) {
			let data = await $.get(url);
			if (!data.content || !data.content.length)
				break;
			let reachedKnown = false;
			for (const x of data.content) {
				let d = new Date(x.datetime);
				if (d.getTime() < newest - 5000) {
					reachedKnown = true;
					continue;
				}
				if (!known.has(logKey(d, x.sender, x.message))) {
					known.add(logKey(d, x.sender, x.message));
					missing.push(x);
				}
			}
			if (reachedKnown || !data.hasMoreData)
				break;
			let oldest = data.content[data.content.length - 1];
			url = _getLogsUrl + "&lastTime=" + encodeURIComponent(oldest.datetime);
		}
		missing.sort((a, b) => (a.position ?? 0) - (b.position ?? 0));
		for (const x of missing)
			renderLogEntry(x.datetime, x.type, x.message, x.sender);
		if (missing.length)
			console.log("Log: " + missing.length + " verpasste Eintraege nachgeladen.");
	} catch (err) {
		console.log(err);
	} finally {
		_catchingUp = false;
	}
}


const COPY_MAX_LINES = 20000;

function copyLine(e, hide) {
	let d = new Date(e.datetime);
	let pad = n => String(n).padStart(2, "0");
	let ts = `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
	let msg = hide ? maskText(e.message) : e.message;
	return `${ts} [${e.sender}] ${e.type} ${msg}`;
}

function copyStatus(text, isError) {
	let el = document.getElementById("logcopystatus");
	if (!el) return;
	el.textContent = text;
	el.style.color = isError ? "#e55" : "";
	clearTimeout(copyStatus._t);
	if (text) copyStatus._t = setTimeout(() => { el.textContent = ""; }, 8000);
}

function dateParam(day) {
	let pad = n => String(n).padStart(2, "0");
	return `${day.getFullYear()}-${pad(day.getMonth() + 1)}-${pad(day.getDate())}`;
}

async function fetchLogsSince(cutoff) {
	let out = [];
	let day = new Date();
	day.setHours(0, 0, 0, 0);
	let cutDay = new Date(cutoff);
	cutDay.setHours(0, 0, 0, 0);
	let truncated = false;
	while (day >= cutDay && !truncated) {
		let base = _getLogsUrl.replace(/date=[^&]*/, "date=" + dateParam(day));
		let lastTime = null;
		let more = true;
		while (more && !truncated) {
			let url = base + (lastTime ? "&lastTime=" + encodeURIComponent(lastTime) : "") + getFilterParameters();
			let data = await $.get(url);
			let content = data.content || [];
			let reachedCutoff = false;
			for (const e of content) {
				if (new Date(e.datetime) < cutoff) { reachedCutoff = true; continue; }
				out.push(e);
			}
			if (out.length >= COPY_MAX_LINES) { truncated = true; break; }
			if (content.length == 0 || reachedCutoff) break;
			lastTime = content[content.length - 1].datetime;
			more = data.hasMoreData;
		}
		day.setDate(day.getDate() - 1);
	}
	return { entries: out, truncated: truncated };
}

async function copyLogs() {
	let btn = document.getElementById("logcopybtn");
	let hours = parseInt(document.getElementById("logcopyrange").value, 10) || 0;
	let hide = isHideData();
	btn.disabled = true;
	try {
		let entries, truncated = false;
		if (hours <= 0) {
			entries = logData.map(x => ({ datetime: x.originalTimestamp, type: x.level, message: x.message, sender: x.sender }));
		} else {
			copyStatus("Loading ...");
			let r = await fetchLogsSince(new Date(Date.now() - hours * 3600 * 1000));
			entries = r.entries;
			truncated = r.truncated;
		}
		if (entries.length == 0) { copyStatus("Nothing to copy.", true); return; }
		let text = entries.slice().reverse().map(e => copyLine(e, hide)).join("\n");
		let ok = await writeClipboard(text);
		let note = `${entries.length} lines` + (truncated ? ` (cut at ${COPY_MAX_LINES}, shorter range recommended)` : "");
		if (ok) copyStatus("Copied: " + note);
		else copyStatus("Clipboard blocked, select the text below: " + note, true);
	} catch (err) {
		copyStatus("Copy failed.", true);
	} finally {
		btn.disabled = false;
	}
}

async function writeClipboard(text) {
	let box = document.getElementById("logcopyfallbackbox");
	let area = document.getElementById("logcopyfallback");
	try {
		if (navigator.clipboard && window.isSecureContext) {
			await navigator.clipboard.writeText(text);
			box.style.display = "none";
			return true;
		}
	} catch (e) {  }
	area.value = text;
	box.style.display = "block";
	area.focus();
	area.select();
	try {
		if (document.execCommand("copy")) { box.style.display = "none"; return true; }
	} catch (e) {  }
	return false;
}
