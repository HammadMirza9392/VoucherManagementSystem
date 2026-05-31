// wwwroot/js/site.js

// Document ready function
$(document).ready(function () {
    // Initialize tooltips
    $('[data-bs-toggle="tooltip"]').tooltip();

    // Initialize popovers
    $('[data-bs-toggle="popover"]').popover();

    // Auto-hide alerts after 5 seconds
    setTimeout(function () {
        $('.alert').fadeOut('slow');
    }, 5000);

    // Confirm delete operations
    $('.btn-danger').on('click', function (e) {
        if ($(this).text().includes('Delete') || $(this).text().includes('Confirm')) {
            if (!confirm('Are you sure you want to delete this item?')) {
                e.preventDefault();
            }
        }
    });

    // Format currency inputs
    $('.currency-input').on('blur', function () {
        var value = parseFloat($(this).val());
        if (!isNaN(value)) {
            $(this).val(value.toFixed(2));
        }
    });

    // Format number inputs
    $('.number-input').on('blur', function () {
        var value = parseFloat($(this).val());
        if (!isNaN(value)) {
            $(this).val(value.toFixed(3));
        }
    });
});

// Utility functions

// Format currency
function formatCurrency(amount) {
    return new Intl.NumberFormat('en-PK', {
        style: 'currency',
        currency: 'PKR',
        minimumFractionDigits: 0,
        maximumFractionDigits: 0
    }).format(amount);
}

// Format date
function formatDate(date) {
    const options = { year: 'numeric', month: 'short', day: 'numeric' };
    return new Date(date).toLocaleDateString('en-US', options);
}

// Show loading spinner - fast version
function showLoading() {
    // Only show if operation takes longer than 100ms
    const loadingHtml = '<div class="loading-overlay" style="opacity:0"><div class="spinner-border spinner-border-sm text-primary" role="status"><span class="visually-hidden">Loading...</span></div></div>';
    $('body').append(loadingHtml);
    // Quick fade in
    setTimeout(() => $('.loading-overlay').css('opacity', '1'), 10);
}

// Hide loading spinner - instant removal
function hideLoading() {
    $('.loading-overlay').remove();
}

// AJAX error handler
$(document).ajaxError(function (event, jqXHR, ajaxSettings, thrownError) {
    hideLoading();
    console.error('AJAX Error:', thrownError);

    if (jqXHR.status === 401) {
        alert('Your session has expired. Please login again.');
        window.location.href = '/Home/Login';
    } else if (jqXHR.status === 403) {
        alert('You do not have permission to perform this action.');
    } else if (jqXHR.status === 404) {
        alert('The requested resource was not found.');
    } else if (jqXHR.status === 500) {
        alert('An error occurred on the server. Please try again later.');
    } else {
        alert('An unexpected error occurred. Please try again.');
    }
});

// AJAX setup for CSRF token
$.ajaxSetup({
    beforeSend: function (xhr, settings) {
        if (settings.type === 'POST' || settings.type === 'PUT' || settings.type === 'DELETE') {
            var token = $('input[name="__RequestVerificationToken"]').val();
            if (token) {
                xhr.setRequestHeader("RequestVerificationToken", token);
            }
        }
    }
});

// Export table to CSV
function exportTableToCSV(tableId, filename) {
    var csv = [];
    var rows = document.querySelectorAll("#" + tableId + " tr");

    for (var i = 0; i < rows.length; i++) {
        var row = [], cols = rows[i].querySelectorAll("td, th");

        for (var j = 0; j < cols.length - 1; j++) { // Exclude last column (actions)
            row.push(cols[j].innerText);
        }

        csv.push(row.join(","));
    }

    // Download CSV file
    downloadCSV(csv.join("\n"), filename);
}

// Download CSV helper
function downloadCSV(csv, filename) {
    var csvFile;
    var downloadLink;

    csvFile = new Blob([csv], { type: "text/csv" });
    downloadLink = document.createElement("a");
    downloadLink.download = filename;
    downloadLink.href = window.URL.createObjectURL(csvFile);
    downloadLink.style.display = "none";
    document.body.appendChild(downloadLink);
    downloadLink.click();
    document.body.removeChild(downloadLink);
}

// Print specific area
function printDiv(divId) {
    var printContents = document.getElementById(divId).innerHTML;
    var originalContents = document.body.innerHTML;

    document.body.innerHTML = printContents;
    window.print();
    document.body.innerHTML = originalContents;
    location.reload();
}

// Debounce function for search inputs
function debounce(func, wait) {
    let timeout;
    return function executedFunction(...args) {
        const later = () => {
            clearTimeout(timeout);
            func(...args);
        };
        clearTimeout(timeout);
        timeout = setTimeout(later, wait);
    };
}

// Dynamic form validation
function validateForm(formId) {
    var form = document.getElementById(formId);
    var inputs = form.querySelectorAll('input[required], select[required], textarea[required]');
    var valid = true;

    inputs.forEach(function (input) {
        if (!input.value.trim()) {
            input.classList.add('is-invalid');
            valid = false;
        } else {
            input.classList.remove('is-invalid');
        }
    });

    return valid;
}

// Number formatting with commas
function numberWithCommas(x) {
    return x.toString().replace(/\B(?=(\d{3})+(?!\d))/g, ",");
}

// Session timeout warning
let sessionTimeout;
let warningTimeout;

function resetSessionTimeout() {
    clearTimeout(sessionTimeout);
    clearTimeout(warningTimeout);

    // Show warning after 25 minutes
    warningTimeout = setTimeout(function () {
        if (confirm('Your session will expire in 5 minutes. Do you want to continue?')) {
            // Make a request to refresh the session
            $.get('/Home/Index');
            resetSessionTimeout();
        }
    }, 25 * 60 * 1000);

    // Logout after 30 minutes
    sessionTimeout = setTimeout(function () {
        window.location.href = '/Home/Logout';
    }, 30 * 60 * 1000);
}

// Initialize session timeout on page load
resetSessionTimeout();

// Reset on user activity
document.addEventListener('click', resetSessionTimeout);
document.addEventListener('keypress', resetSessionTimeout);

// Loading style - optimized for fast display
const loadingStyle = `
<style>
.loading-overlay {
    position: fixed;
    top: 0;
    left: 0;
    width: 100%;
    height: 100%;
    background-color: rgba(0, 0, 0, 0.3);
    display: flex;
    justify-content: center;
    align-items: center;
    z-index: 9999;
    transition: opacity 0.15s ease-in;
}
.loading-overlay .spinner-border-sm {
    width: 2rem;
    height: 2rem;
}
</style>
`;

// Add loading style to head
$('head').append(loadingStyle);