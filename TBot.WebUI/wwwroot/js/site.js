

function showLoading() {
	$("#backdrop").show();
}

function hideLoading() {
	$("#backdrop").hide();
}

window.tbotHideData = {
	get: function () {
		try { return localStorage.getItem("tbot.hideData") === "1"; } catch (e) { return false; }
	},
	set: function (on) {
		try { localStorage.setItem("tbot.hideData", on ? "1" : "0"); } catch (e) { }
	}
};
