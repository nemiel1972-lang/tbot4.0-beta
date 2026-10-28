

function showLoading() {
	$("#backdrop").show();
}

function hideLoading() {
	$("#backdrop").hide();
}

window.tbotHideData = {
	get: function () {
		try {
			var v = localStorage.getItem("tbot.hideData");
			if (v === "1") return true;
			if (v === "0") return false;
		} catch (e) { }
		return !!window.tbotHideDefault;
	},
	set: function (on) {
		try {
			if (!!on === !!window.tbotHideDefault) localStorage.removeItem("tbot.hideData");
			else localStorage.setItem("tbot.hideData", on ? "1" : "0");
		} catch (e) { }
	}
};
