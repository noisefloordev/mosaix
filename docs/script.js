// GA.  Check the host, so if somebody clones the repository and doesn't change/disable
// this, we don't send bogus GA requests.
if(document.location.host == "noisefloordev.github.io")
{
    (function(i,s,o,g,r,a,m){i['GoogleAnalyticsObject']=r;i[r]=i[r]||function(){
      (i[r].q=i[r].q||[]).push(arguments)},i[r].l=1*new Date();a=s.createElement(o),
      m=s.getElementsByTagName(o)[0];a.async=1;a.src=g;m.parentNode.insertBefore(a,m)
      })(window,document,'script','https://www.google-analytics.com/analytics.js','ga');

      ga('create', 'UA-99113878-1', 'auto');
      ga('send', 'pageview');
}

(function()
{
    function closest(e, selector)
    {
        while(e)
        {
            if(e.matches(selector))
                return e;
            e = e.parentElement;
        }
        return null;
    }
/*
    var selected_tab = null;
    function select_tab(tab)
    {
        if(selected_tab != null)
        {
            document.querySelector("[data-tab=" + selected_tab + "]").hidden = true;
            delete document.querySelector("[data-tab-button=" + selected_tab + "]").dataset.active;
        }
        selected_tab = tab;
        document.querySelector("[data-tab=" + selected_tab + "]").hidden = false;
        document.querySelector("[data-tab-button=" + selected_tab + "]").dataset.active = "selected";
    };

    window.addEventListener("load", function() {
        select_tab("intro");
        document.querySelector(".header").addEventListener("click", function(e) {
            var button = closest(e.target, "[data-tab-button]");
            if(button == null)
                return;

            var tab = button.dataset.tabButton;
            select_tab(tab);
        });
    });
*/
})();

